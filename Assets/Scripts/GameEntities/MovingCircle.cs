using Unity.Netcode;
using UnityEngine;

public struct CircleState : INetworkSerializeByMemcpy
{
    public Vector2 position;
    public Vector2 velocity;
    public int tick; // The specific tick this state was calculated on
}

public class MovingCircle : NetworkBehaviour
{
    [SerializeField] private float m_Radius = 1;

    public Vector2 InitialPosition;
    public Vector2 InitialVelocity;

    private NetworkVariable<CircleState> m_ServerState = new NetworkVariable<CircleState>();
    private GameState m_GameState;

    private Vector2 m_PredictedPosition;
    private Vector2 m_PredictedVelocity;

    private bool m_HasNewServerState;
    private CircleState m_LatestServerState;

    public Vector2 Position => IsServer ? m_ServerState.Value.position : m_PredictedPosition;
    public Vector2 Velocity => IsServer ? m_ServerState.Value.velocity : m_PredictedVelocity;

    private void Awake()
    {
        m_GameState = FindFirstObjectByType<GameState>();
    }

    public override void OnNetworkSpawn()
    {
        NetworkManager.NetworkTickSystem.Tick += OnNetworkTick;

        if (IsServer)
        {
            m_ServerState.Value = new CircleState
            {
                position = InitialPosition,
                velocity = InitialVelocity,
                tick = NetworkManager.ServerTime.Tick
            };
        }
        else
        {
            m_PredictedPosition = InitialPosition;
            m_PredictedVelocity = InitialVelocity;
            m_ServerState.OnValueChanged += OnServerStateChanged;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (NetworkManager != null && NetworkManager.NetworkTickSystem != null)
            NetworkManager.NetworkTickSystem.Tick -= OnNetworkTick;

        if (!IsServer)
        {
            m_ServerState.OnValueChanged -= OnServerStateChanged;
        }
    }

    private void OnServerStateChanged(CircleState oldState, CircleState newState)
    {
        m_LatestServerState = newState;
        m_HasNewServerState = true;
    }

    private void OnNetworkTick()
    {
        float delta = 1f / NetworkManager.NetworkTickSystem.TickRate;

        if (IsServer)
        {
            int currentServerTick = NetworkManager.ServerTime.Tick;

            if (!m_GameState.IsStunnedAtTick(currentServerTick))
            {
                var state = m_ServerState.Value;
                state = SimulateStep(state, delta);
                state.tick = currentServerTick;
                m_ServerState.Value = state;
            }
        }
        else
        {
            int localTick = NetworkUtility.GetLocalTick();
            int serverTick = NetworkManager.ServerTime.Tick;

            if (m_HasNewServerState)
            {
                Reconciliate(localTick, delta);
                m_HasNewServerState = false;
            }

            // ✅ serverTick pour le stun, pas localTick
            if (!m_GameState.IsStunnedAtTick(serverTick))
            {
                CircleState predicted = new CircleState
                {
                    position = m_PredictedPosition,
                    velocity = m_PredictedVelocity
                };
                predicted = SimulateStep(predicted, delta);
                m_PredictedPosition = predicted.position;
                m_PredictedVelocity = predicted.velocity;
            }
        }
    }

    private void Reconciliate(int targetTick, float delta)
    {
        CircleState state = m_LatestServerState;

        // fast-forward jusqu'à targetTick - 1 inclus
        // le step normal dans OnNetworkTick avancera jusqu'à targetTick
        int ticksToSimulate = targetTick - state.tick - 1;

        for (int i = 0; i < ticksToSimulate; i++)
        {
            int simulationTick = state.tick + i;
            if (!m_GameState.IsStunnedAtTick(simulationTick))
            {
                state = SimulateStep(state, delta);
            }
        }

        m_PredictedPosition = state.position;
        m_PredictedVelocity = state.velocity;
    }

    private CircleState SimulateStep(CircleState state, float delta)
    {
        state.position += state.velocity * delta;

        var size = m_GameState.GameSize;
        if (state.position.x - m_Radius < -size.x)
        {
            state.position = new Vector2(-size.x + m_Radius, state.position.y);
            state.velocity *= new Vector2(-1, 1);
        }
        else if (state.position.x + m_Radius > size.x)
        {
            state.position = new Vector2(size.x - m_Radius, state.position.y);
            state.velocity *= new Vector2(-1, 1);
        }

        if (state.position.y + m_Radius > size.y)
        {
            state.position = new Vector2(state.position.x, size.y - m_Radius);
            state.velocity *= new Vector2(1, -1);
        }
        else if (state.position.y - m_Radius < -size.y)
        {
            state.position = new Vector2(state.position.x, -size.y + m_Radius);
            state.velocity *= new Vector2(1, -1);
        }

        return state;
    }
}