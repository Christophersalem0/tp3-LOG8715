using Unity.Netcode;
using UnityEngine;
using System.Collections.Generic;

// InputTick — stunTick n'est plus nécessaire
public struct InputTick : INetworkSerializeByMemcpy
{
    public Vector2 input;
    public bool stunPressed;
    public int tick;      // local tick — réconciliation mouvement
    public int stunTick;  // server tick — vérification stun
}

public struct StateTick : INetworkSerializeByMemcpy
{
    public InputTick input;
    public Vector2 position;
}

public class Player : NetworkBehaviour
{
    [SerializeField] private float m_Velocity = 5f;
    [SerializeField] private float m_Size = 1f;

    private GameState m_GameState;
    private StateTick m_PredictedState;

    private float TickDelta => 1f / NetworkManager.NetworkTickSystem.TickRate;

    private NetworkVariable<StateTick> m_ServerState = new NetworkVariable<StateTick>();

    public Vector2 Position => (IsClient && IsOwner) ? m_PredictedState.position : m_ServerState.Value.position;

    private Queue<InputTick> m_InputQueue = new Queue<InputTick>();
    private List<StateTick> m_StateHistory = new List<StateTick>(); // Changed to List for easier history management

    private bool m_StunBuffered;
    private bool m_HasNewServerState;
    private StateTick m_LatestServerState;

    private void Awake()
    {
        m_GameState = FindFirstObjectByType<GameState>();
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;
        m_ServerState.OnValueChanged += OnServerStateChanged;

        if (IsOwner)
        {
            m_PredictedState = new StateTick { position = transform.position };
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;
        m_ServerState.OnValueChanged -= OnServerStateChanged;
    }

    private void Update()
    {
        // Buffer input so we don't miss keydowns between tick intervals
        if (IsClient && IsOwner && Input.GetKeyDown(KeyCode.Space))
        {
            m_StunBuffered = true;
        }
    }

    private void OnServerStateChanged(StateTick oldState, StateTick newState)
    {
        if (IsClient && IsOwner)
        {
            m_LatestServerState = newState;
            m_HasNewServerState = true; // Flag for OnNetworkTick to handle
        }
    }

    private void OnNetworkTick()
    {
        if (m_GameState == null) return;

        if (IsServer)
            UpdatePositionServer();

        if (IsClient && IsOwner)
        {
            if (m_HasNewServerState)
            {
                Reconciliate();
                m_HasNewServerState = false;
            }
            UpdateInputClient();
        }
    }

    private void UpdatePositionServer()
    {
        while (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();
            int localTick = NetworkUtility.GetLocalTick();    // tick du client — pour la réconciliation
            int serverTick = NetworkManager.ServerTime.Tick;

            if (input.stunPressed)
                m_GameState.ApplyStun(input.tick);  // ← toujours le tick serveur courant

            StateTick state = m_ServerState.Value;

            if (!m_GameState.IsStunnedAtTick(input.tick))  // ← même tick
                state.position += input.input * m_Velocity * TickDelta;

            state.input = input;
            state.position = ClampToGameArea(state.position);
            m_ServerState.Value = state;
        }
    }
    private void UpdateInputClient()
    {
        Vector2 inputDirection = Vector2.zero;
        if (Input.GetKey(KeyCode.W)) inputDirection += Vector2.up;
        if (Input.GetKey(KeyCode.A)) inputDirection += Vector2.left;
        if (Input.GetKey(KeyCode.S)) inputDirection += Vector2.down;
        if (Input.GetKey(KeyCode.D)) inputDirection += Vector2.right;
        inputDirection = inputDirection.normalized;

        bool stunPressed = m_StunBuffered;
        m_StunBuffered = false;

        int localTick = NetworkUtility.GetLocalTick();
        int serverTick = NetworkManager.ServerTime.Tick;

        InputTick input = new InputTick
        {
            input = inputDirection,
            stunPressed = stunPressed,
            tick = localTick,
            stunTick = serverTick,  // ← stocké pour PredictPosition
        };

        SendInputServerRpc(input);

        if (stunPressed)
            m_GameState.ApplyStun(localTick);

        PredictPosition(input);
    }

    private void PredictPosition(InputTick input)
    {
        // ← utilise stunTick (server-space) au lieu de tick (local-space)
        if (!m_GameState.IsStunnedAtTick(input.tick))
        {
            m_PredictedState.position += input.input * m_Velocity * TickDelta;
        }

        m_PredictedState.input = input;
        m_PredictedState.position = ClampToGameArea(m_PredictedState.position);
        m_StateHistory.Add(m_PredictedState);
    }

    private void Reconciliate()
    {
        // 1. Cull old predictions up to the tick the server just confirmed
        m_StateHistory.RemoveAll(s => s.input.tick <= m_LatestServerState.input.tick);

        // 2. Snap to the authoritative server state
        m_PredictedState = m_LatestServerState;

        // 3. Re-simulate the pending queue. Stun states will naturally evaluate correctly here.
        List<StateTick> oldHistory = new List<StateTick>(m_StateHistory);
        m_StateHistory.Clear();

        foreach (var state in oldHistory)
        {
            PredictPosition(state.input);
        }
    }

    private Vector2 ClampToGameArea(Vector2 pos)
    {
        var size = m_GameState.GameSize;
        pos.x = Mathf.Clamp(pos.x, -size.x + m_Size, size.x - m_Size);
        pos.y = Mathf.Clamp(pos.y, -size.y + m_Size, size.y - m_Size);
        return pos;
    }

    [ServerRpc]
    private void SendInputServerRpc(InputTick input)
    {
        m_InputQueue.Enqueue(input);
    }
}