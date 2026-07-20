using System;
using System.Collections.Generic;

using SideXP.Core.EditorOnly;

using UnityEditor;
using UnityEditor.IMGUI.Controls;

using UnityEngine;

namespace SideXP.Broadcaster.EditorOnly
{

    /// <summary>
    /// Draws a field marked with <see cref="EventTypeRefAttribute"/> as a searchable picker over the project's signal and cue types, and
    /// keeps the stored <see cref="Type.AssemblyQualifiedName"/> in sync with the referenced type's current name (so a wiring survives a
    /// rename of the event type).
    /// </summary>
    [CustomPropertyDrawer(typeof(EventTypeRefAttribute))]
    public class EventTypeRefDrawer : PropertyDrawer
    {

        /// <summary>
        /// The searchable dropdown listing the project's signal and cue types, grouped by kind, plus a "(None)" option to clear the field.
        /// </summary>
        private class EventTypeDropdown : AdvancedDropdown
        {

            /// <summary>
            /// An item that carries the catalog entry it stands for (<c>null</c> for the "(None)" option). The entry is read back off the
            /// selected item directly, because Unity reassigns <see cref="AdvancedDropdownItem.id"/> internally while building the tree, so an
            /// external id-to-entry map can't be trusted.
            /// </summary>
            private class EventTypeItem : AdvancedDropdownItem
            {

                /// <summary>The entry this item selects, or <c>null</c> to clear the field.</summary>
                public EventEntry Entry { get; }

                /// <inheritdoc cref="EventTypeItem"/>
                public EventTypeItem(string name, EventEntry entry) : base(name)
                {
                    Entry = entry;
                }

            }

            /// <summary>
            /// The objects being edited. Captured instead of the <see cref="SerializedProperty"/> because selection happens on a later
            /// frame, by when the property (and its <see cref="SerializedObject"/>) rebuilt by the inspector each frame is stale.
            /// </summary>
            private readonly UnityEngine.Object[] _targets;

            /// <summary>The path of the edited property, used to re-acquire it from a fresh <see cref="SerializedObject"/> on selection.</summary>
            private readonly string _propertyPath;

            /// <inheritdoc cref="EventTypeDropdown"/>
            public EventTypeDropdown(AdvancedDropdownState state, SerializedProperty property) : base(state)
            {
                _targets = property.serializedObject.targetObjects;
                _propertyPath = property.propertyPath;
                minimumSize = new Vector2(0f, 240f);
            }

            /// <inheritdoc cref="AdvancedDropdown.BuildRoot"/>
            protected override AdvancedDropdownItem BuildRoot()
            {
                AdvancedDropdownItem root = new AdvancedDropdownItem("Signals & Cues");
                root.AddChild(new EventTypeItem("(None)", null));

                List<EventEntry> catalog = EventCatalog.Build();
                AddKind(root, catalog, EventKind.Signal, "Signals");
                AddKind(root, catalog, EventKind.Cue, "Cues");
                return root;
            }

            /// <summary>
            /// Adds a group for a single kind under the root, with one leaf per catalog entry of that kind (hidden ones excluded). The group
            /// header is created lazily so a kind with no entries produces nothing.
            /// </summary>
            private void AddKind(AdvancedDropdownItem root, List<EventEntry> catalog, EventKind kind, string title)
            {
                AdvancedDropdownItem group = null;
                foreach (EventEntry entry in catalog)
                {
                    if (entry.Kind != kind || entry.Hidden)
                        continue;

                    if (group == null)
                    {
                        group = new AdvancedDropdownItem(title);
                        root.AddChild(group);
                    }

                    group.AddChild(new EventTypeItem(entry.DisplayName, entry));
                }
            }

            /// <inheritdoc cref="AdvancedDropdown.ItemSelected(AdvancedDropdownItem)"/>
            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                EventEntry entry = (item as EventTypeItem)?.Entry;
                // Resolve(Type) also registers the type with the migration tracker, so the stored name follows a later rename.
                string value = entry != null ? TypesMigration.Resolve(entry.EventType) : string.Empty;

                // Write through a fresh SerializedObject: the one that opened this dropdown was discarded when the inspector repainted.
                SerializedObject serializedObject = new SerializedObject(_targets);
                SerializedProperty property = serializedObject.FindProperty(_propertyPath);
                if (property != null)
                {
                    property.stringValue = value;
                    serializedObject.ApplyModifiedProperties();
                }
            }

        }

        /// <inheritdoc cref="PropertyDrawer.OnGUI(Rect, SerializedProperty, GUIContent)"/>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, $"Use {nameof(EventTypeRefAttribute)} on string fields only.");
                return;
            }

            if (property.hasMultipleDifferentValues)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            // Resolve the stored name for display, following a rename when the type is tracked and rewriting the stored value to match.
            Type resolved = ResolveType(property.stringValue);
            if (resolved != null && property.stringValue != resolved.AssemblyQualifiedName)
            {
                property.stringValue = resolved.AssemblyQualifiedName;
                property.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            }

            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Keyboard), label);

            GUIContent content = new GUIContent(DisplayLabel(resolved, property.stringValue));
            if (EditorGUI.DropdownButton(position, content, FocusType.Keyboard))
            {
                EventTypeDropdown dropdown = new EventTypeDropdown(new AdvancedDropdownState(), property);
                dropdown.Show(position);
            }
        }

        /// <summary>
        /// Resolves the type behind a stored assembly-qualified name. Prefers Core's migration tracker so a renamed type is still found by an
        /// old name, and falls back to a direct <see cref="Type.GetType(string)"/> for a type the tracker doesn't know: it only tracks types
        /// it can map back from their declaring script, which fails when a script declares several types (common for events).
        /// </summary>
        private static Type ResolveType(string stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return null;

            return TypesMigration.Resolve(stored, out Type tracked) ? tracked : Type.GetType(stored);
        }

        /// <summary>
        /// The text shown on the dropdown button: the type's catalog display name when it resolves, a "(None)" placeholder when the field is
        /// empty, or a "(Missing)" marker when a stored name no longer matches any type.
        /// </summary>
        private static string DisplayLabel(Type type, string stored)
        {
            if (type != null)
                return EventCatalog.TryClassify(type, out EventEntry entry) ? entry.DisplayName : type.Name;

            return string.IsNullOrWhiteSpace(stored) ? "(None)" : "(Missing type)";
        }

    }

}
