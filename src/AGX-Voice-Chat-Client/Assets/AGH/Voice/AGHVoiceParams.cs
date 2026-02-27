using JetBrains.Annotations;

namespace AGH.Voice
{
    /// <summary>
    /// Parameters passed to the AGH Dissonance comms when starting as client or host.
    /// </summary>
    public class AGHVoiceParams
    {
        [NotNull] public IAGHVoiceTransport Transport { get; }

        public AGHVoiceParams([NotNull] IAGHVoiceTransport transport)
        {
            Transport = transport ?? throw new System.ArgumentNullException(nameof(transport));
        }
    }
}
