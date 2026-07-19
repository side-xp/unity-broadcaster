using System;

namespace SideXP.Broadcaster
{

    /// <summary>
    /// A callback-style cue performer, for coroutine/callback code that doesn't want to author an
    /// <see cref="UnityEngine.Awaitable"/>. It starts synchronously when the cue is sent and reports completion by invoking the
    /// <paramref name="done"/> callback it's handed.<br/>
    /// </summary>
    /// <typeparam name="T">The exact cue type this performer reacts to.</typeparam>
    /// <param name="cue">The cue instance (its fields are the payload).</param>
    /// <param name="done">Call this once your reaction has finished (the cue's completion waits for it). It must be called eventually
    /// (or the cue never completes for this performer, until the performer is unregistered). Calling it more than once is harmless.</param>
    public delegate void CuePerformerDelegate<T>(T cue, Action done) where T : ICue;

}
