using Unity.Netcode;
using UnityEngine;

namespace Code
{
    public class CodePlayer : NetworkBehaviour
    {
        public NetworkVariable<int> CurrentMode = new NetworkVariable<int>(0); // 0: Server, 1: Rewind, 2: Client
        public NetworkVariable<Vector3> CustomPosition = new NetworkVariable<Vector3>();

        private Unity.Netcode.Components.NetworkTransform m_NetTransform;
        private float m_ClientVerticalVelocity = 0f;
        private bool m_ClientJumpRequested;

        private float m_VerticalVelocity = 0f;
        private Vector2 m_ServerInput;
        private bool m_JumpRequested;
        private Vector2 m_LastInput;
        private float mapLimitX = 5f; // Límite en el eje X (izquierda/derecha)
        private float mapLimitZ = 5f; // Límite en el eje Z (arriba/abajo)

        public NetworkVariable<int> CurrentEffect = new NetworkVariable<int>(0); // 0: Normal, 1: Rápido(Verde), 2: Lento(Rojo)

        private float m_BaseSpeed = 5f;
        private Renderer m_Renderer;
        public float currentSpeed;

        private void Awake()
        {
            m_Renderer = GetComponent<Renderer>();
            m_NetTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        }

        public override void OnNetworkSpawn()
        {
            CurrentEffect.OnValueChanged += OnEffectChanged;
            UpdateVisuals(CurrentEffect.Value);
        }

        public override void OnNetworkDespawn()
        {
            CurrentEffect.OnValueChanged -= OnEffectChanged;
        }

        private void OnEffectChanged(int previousValue, int newValue)
        {
            UpdateVisuals(newValue);
        }

        private void UpdateVisuals(int effectIndex)
        {
            if (m_Renderer == null) return;

            if (effectIndex == 1) m_Renderer.material.color = Color.green;
            else if (effectIndex == 2) m_Renderer.material.color = Color.red;
            else m_Renderer.material.color = Color.grey; // Color por defecto
        }

        [Rpc(SendTo.Server)]
        private void SendInputServerRpc(Vector2 input, bool jump)
        {
            m_ServerInput = input;
            if (jump) m_JumpRequested = true;
        }

        [Rpc(SendTo.Server)]
        public void CycleModeServerRpc()
        {
            CurrentMode.Value = (CurrentMode.Value + 1) % 3; // Cicla entre 0, 1 y 2
        }

        [Rpc(SendTo.Server)]
        private void UpdateClientAuthorityPositionServerRpc(Vector3 clientPos)
        {
            transform.position = clientPos;
            CustomPosition.Value = transform.position;
        }

        private void SimulateMovement(Vector2 input, ref bool jumpRequested, ref float verticalVelocity)
        {
            currentSpeed = m_BaseSpeed;
            if (CurrentEffect.Value == 1) currentSpeed = m_BaseSpeed * 2f;      // Ventaja: Doble de rápido
            else if (CurrentEffect.Value == 2) currentSpeed = m_BaseSpeed * 0.5f; // Desventaja: Mitad de rápido

            Vector3 moveDir = new Vector3(input.x, 0, input.y).normalized;
            transform.Translate(moveDir * (currentSpeed * Time.deltaTime), Space.World);

            if (transform.position.y > 1f || jumpRequested)
            {
                if (jumpRequested && transform.position.y <= 1.05f)
                {
                    verticalVelocity = 5f;
                    jumpRequested = false;
                }
                verticalVelocity += Physics.gravity.y * Time.deltaTime;
                transform.Translate(Vector3.up * (verticalVelocity * Time.deltaTime), Space.World);

                if (transform.position.y < 1f)
                {
                    transform.position = new Vector3(transform.position.x, 1f, transform.position.z);
                    verticalVelocity = 0f;
                }
            }
            else
            {
                jumpRequested = false;
            }

            // Límites del plano
            float clampedX = Mathf.Clamp(transform.position.x, -mapLimitX, mapLimitX);
            float clampedZ = Mathf.Clamp(transform.position.z, -mapLimitZ, mapLimitZ);
            transform.position = new Vector3(clampedX, transform.position.y, clampedZ);
        }

        private void Update()
        {
            // Activamos NetworkTransform solo en el Modo 0 (Server Auth estándar)
            if (m_NetTransform != null)
            {
                m_NetTransform.enabled = (CurrentMode.Value == 0);
            }

            // 1. EL DUEÑO (Owner) procesa inputs y predicciones locales
            if (IsOwner)
            {
                Vector2 input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                bool jump = Input.GetButtonDown("Jump");

                if (IsServer)
                {
                    m_ServerInput = input;
                    if (jump) m_JumpRequested = true;
                }
                else if (input != m_LastInput || jump)
                {
                    SendInputServerRpc(input, jump);
                    m_LastInput = input;
                }

                // MODO 1: Autoridad en Servidor con Rewind (Predicción en Cliente)
                if (CurrentMode.Value == 1)
                {
                    if (jump) m_ClientJumpRequested = true;
                    SimulateMovement(input, ref m_ClientJumpRequested, ref m_ClientVerticalVelocity);

                    // REWIND / RECONCILIATION: Si nos alejamos demasiado de la verdad del servidor, rebobinamos
                    if (Vector3.Distance(transform.position, CustomPosition.Value) > 0.3f)
                    {
                        transform.position = CustomPosition.Value;
                    }
                }
                // MODO 2: Autoridad en el Cliente
                else if (CurrentMode.Value == 2)
                {
                    if (jump) m_ClientJumpRequested = true;
                    SimulateMovement(input, ref m_ClientJumpRequested, ref m_ClientVerticalVelocity);
                    UpdateClientAuthorityPositionServerRpc(transform.position); // El cliente impone su posición
                }
            }

            // 2. EL SERVIDOR procesa la autoridad para los Modos 0 y 1
            if (IsServer)
            {
                if (CurrentMode.Value == 0 || CurrentMode.Value == 1)
                {
                    SimulateMovement(m_ServerInput, ref m_JumpRequested, ref m_VerticalVelocity);
                    CustomPosition.Value = transform.position; // Guardamos la posición oficial
                }
            }

            // 3. REMOTOS (Visualización de movimientos en Modos 1 y 2 para otros jugadores)
            if (!IsOwner && CurrentMode.Value != 0)
            {
                transform.position = CustomPosition.Value;
            }
        }
    }
}