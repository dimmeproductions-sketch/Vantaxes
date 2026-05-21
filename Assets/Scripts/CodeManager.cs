using Unity.Netcode;
using UnityEngine;

namespace Code
{
    public class CodeManager : MonoBehaviour
    {
        private NetworkManager m_NetworkManager;

        private float m_GlobalEffectTimer = 0f;
        private float m_EffectDurationTimer = 0f;
        private bool m_IsEffectActive = false;
        private CodePlayer m_SelectedPlayer = null;

        private void Awake()
        {
            m_NetworkManager = GetComponent<NetworkManager>();
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(10, 10, 300, 300));
            if (!m_NetworkManager.IsClient && !m_NetworkManager.IsServer)
            {
                StartButtons();
            }
            else
            {
                StatusLabels();
                SubmitModeChange();
            }

            GUILayout.EndArea();
        }

        private void StartButtons()
        {
            if (GUILayout.Button("Host")) m_NetworkManager.StartHost();
            if (GUILayout.Button("Client")) m_NetworkManager.StartClient();
            if (GUILayout.Button("Server")) m_NetworkManager.StartServer();
        }

        private void StatusLabels()
        {
            var mode = m_NetworkManager.IsHost ?
                "Host" : m_NetworkManager.IsServer ? "Server" : "Client";

            GUILayout.Label("Transport: " + m_NetworkManager.NetworkConfig.NetworkTransport.GetType().Name);
            GUILayout.Label("Mode: " + mode);
            GUILayout.Label($"Jugadores: {m_NetworkManager.ConnectedClientsIds.Count}");
        }

        private void SubmitModeChange()
        {
            var playerObject = m_NetworkManager.SpawnManager.GetLocalPlayerObject();
            if (playerObject != null)
            {
                var player = playerObject.GetComponent<CodePlayer>();
                // Traducimos el número de modo actual a un texto legible
                string modeName = player.CurrentMode.Value == 0 ? "Server Auth" :
                                  (player.CurrentMode.Value == 1 ? "Server Auth + Rewind" : "Client Auth");

                if (GUILayout.Button($"Modo: {modeName} (Click para cambiar)"))
                {
                    player.CycleModeServerRpc();
                }
            }
        }

        private void Update()
        {
            // Solo el servidor lleva la cuenta del tiempo global
            if (m_NetworkManager == null || !m_NetworkManager.IsServer) return;

            if (!m_IsEffectActive)
            {
                m_GlobalEffectTimer += Time.deltaTime;
                if (m_GlobalEffectTimer >= 20f)
                {
                    ActivateRandomEffect();
                }
            }
            else
            {
                m_EffectDurationTimer += Time.deltaTime;
                if (m_EffectDurationTimer >= 10f)
                {
                    DeactivateRandomEffect();
                }
            }
        }

        private void ActivateRandomEffect()
        {
            var clients = m_NetworkManager.ConnectedClientsList;
            if (clients.Count == 0) return;

            // 1. Elegimos un índice al azar de la lista de clientes conectados
            int randomIndex = Random.Range(0, clients.Count);
            var targetClient = clients[randomIndex];

            if (targetClient != null && targetClient.PlayerObject != null)
            {
                // 2. Obtenemos el script CodePlayer de ese jugador elegido
                m_SelectedPlayer = targetClient.PlayerObject.GetComponent<CodePlayer>();

                if (m_SelectedPlayer != null)
                {
                    // 3. Le aplicamos el efecto (1: Ventaja, 2: Desventaja)
                    m_SelectedPlayer.CurrentEffect.Value = Random.Range(1, 3);

                    m_IsEffectActive = true;
                    m_GlobalEffectTimer = 0f;
                    m_EffectDurationTimer = 0f;
                }
            }
        }

        private void DeactivateRandomEffect()
        {
            // Si el jugador seleccionado sigue en la partida, le quitamos el efecto
            if (m_SelectedPlayer != null)
            {
                m_SelectedPlayer.CurrentEffect.Value = 0;
                m_SelectedPlayer = null;
            }

            m_IsEffectActive = false;
            m_GlobalEffectTimer = 0f;
            m_EffectDurationTimer = 0f;
        }
    }
}