using System.Collections.Generic;
using Dissonance;
using TMPro;
using UnityEngine;

namespace AGH.Voice
{
    /// <summary>
    /// Displays Dissonance voice debug info on a Unity UI Text: device, peers, volume, connection, and other available state.
    /// Assign <see cref="comms"/> and optionally <see cref="debugText"/>. If debugText is null, tries to find or create one on this GameObject.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class DissonanceDebugOverlay : MonoBehaviour
    {
        [SerializeField] [Tooltip("Dissonance comms to read state from. Auto-finds on this GameObject if unset.")]
        private DissonanceComms comms;

        [SerializeField] [Tooltip("UI Text to display debug info. Auto-finds or creates on this GameObject if unset.")]
        private TextMeshProUGUI debugText;

        [SerializeField] [Tooltip("Optional AGH voice network for connection status. Auto-finds on this GameObject if unset.")]
        private AGHCommsNetwork aghNetwork;

        [SerializeField] [Tooltip("Refresh interval in seconds.")]
        private float refreshInterval = 0.25f;

        private float _nextRefresh;

        private void Awake()
        {
            if (comms == null) comms = GetComponent<DissonanceComms>();
            if (aghNetwork == null) aghNetwork = GetComponent<AGHCommsNetwork>();
            if (debugText == null)
            {
                debugText = GetComponent<TextMeshProUGUI>();
                if (debugText == null)
                {
                    debugText = gameObject.AddComponent<TextMeshProUGUI>();
                    debugText.fontSize = 14;
                    debugText.color = Color.white;
                }
            }
        }

        private void Update()
        {
            if (debugText == null || Time.unscaledTime < _nextRefresh)
                return;
            _nextRefresh = Time.unscaledTime + refreshInterval;

            debugText.text = BuildDebugString();
        }

        private string BuildDebugString()
        {
            var lines = new List<string>();

            if (comms == null)
            {
                lines.Add("[Dissonance] No DissonanceComms assigned.");
                return string.Join("\n", lines);
            }

            // --- Device ---
            lines.Add("--- Device ---");
            string micName = comms.MicrophoneName;
            lines.Add($"Microphone: {(string.IsNullOrEmpty(micName) ? "(default)" : micName)}");
            var devices = new List<string>();
            comms.GetMicrophoneDevices(devices);
            lines.Add($"Available devices: {devices.Count}");
            for (int i = 0; i < devices.Count && i < 5; i++)
                lines.Add($"  [{i}] {devices[i]}");
            if (devices.Count > 5)
                lines.Add($"  ... and {devices.Count - 5} more");

            // --- Connection / network ---
            lines.Add("");
            lines.Add("--- Connection ---");
            lines.Add($"Network initialized: {comms.IsNetworkInitialized}");
            if (aghNetwork != null)
            {
                lines.Add($"Status: {aghNetwork.Status}");
                lines.Add($"Mode: {aghNetwork.Mode}");
            }
            lines.Add($"Local player: {comms.LocalPlayerName ?? "(not set)"}");

            // --- Volume & mute ---
            lines.Add("");
            lines.Add("--- Volume ---");
            lines.Add($"Remote voice volume: {comms.RemoteVoiceVolume:F2}");
            lines.Add($"Muted: {comms.IsMuted}");
            lines.Add($"Deafened: {comms.IsDeafened}");

            // --- Peers ---
            lines.Add("");
            lines.Add("--- Peers ---");
            var players = comms.Players;
            if (players == null)
                lines.Add("Players: (null)");
            else
            {
                lines.Add($"Count: {players.Count}");
                foreach (var p in players)
                {
                    if (p == null) continue;
                    string localTag = p.IsLocalPlayer ? " [YOU]" : "";
                    string speaking = p.IsSpeaking ? " SPEAKING" : "";
                    string conn = p.IsConnected ? "" : " (disconnected)";
                    string vol = p.IsLocalPlayer ? "" : $" vol={p.Volume:F2}";
                    string pl = p.PacketLoss.HasValue ? $" loss={p.PacketLoss.Value:P1}" : "";
                    string amp = p.IsSpeaking ? $" amp={p.Amplitude:F2}" : "";
                    string rooms = p.Rooms != null && p.Rooms.Count > 0 ? $" rooms=[{string.Join(",", p.Rooms)}]" : "";
                    lines.Add($"  • {p.Name}{localTag}{speaking}{conn}{vol}{pl}{amp}{rooms}");
                }
            }

            // --- Channels (summary) ---
            lines.Add("");
            lines.Add("--- Channels ---");
            int roomCount = comms.Rooms != null ? comms.Rooms.Count : 0;
            var localPlayerRooms = comms.FindPlayer(comms.LocalPlayerName ?? "");
            string roomList = localPlayerRooms?.Rooms != null && localPlayerRooms.Rooms.Count > 0
                ? string.Join(", ", localPlayerRooms.Rooms)
                : "(none)";
            lines.Add($"Rooms (listening): {roomList} (count={roomCount})");
            lines.Add($"Player channels open: {comms.PlayerChannels?.Count ?? 0}");
            lines.Add($"Room channels open: {comms.RoomChannels?.Count ?? 0}");

            // --- Other ---
            lines.Add("");
            lines.Add("--- Other ---");
            lines.Add($"Player priority: {comms.PlayerPriority}");
            lines.Add($"Top priority speaker: {comms.TopPrioritySpeaker}");
            var localPlayer = comms.FindPlayer(comms.LocalPlayerName ?? "");
            if (localPlayer != null && localPlayer.PacketLoss.HasValue)
                lines.Add($"Local packet loss: {localPlayer.PacketLoss.Value:P1}");

            return string.Join("\n", lines);
        }
    }
}
