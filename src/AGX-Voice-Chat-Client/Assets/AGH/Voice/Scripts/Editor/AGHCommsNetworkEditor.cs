using UnityEditor;
using UnityEngine;

namespace AGH.Voice.Editor
{
    [CustomEditor(typeof(AGHCommsNetwork))]
    public class AGHCommsNetworkEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "Unity is client only. Assign an IAGHVoiceTransport (e.g. AGHVoiceTransportAdapter) and call RunAsClient(transport) when connected to AGH.Server. " +
                "Stop with Stop() or when disabled. Set DissonanceComms' Comms Network to this component.",
                MessageType.Info);
        }
    }
}
