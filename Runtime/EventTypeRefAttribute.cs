using UnityEngine;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// Marks a serialized <see cref="string"/> field as holding the <see cref="System.Type.AssemblyQualifiedName"/> of a Broadcaster event
    /// type (a signal or a cue).<br/>
    /// In the inspector, the field is edited through a searchable picker sourced from the project's event catalog, and its stored value is
    /// kept valid across renames of the referenced event type.
    /// </summary>
    public class EventTypeRefAttribute : PropertyAttribute { }

}
