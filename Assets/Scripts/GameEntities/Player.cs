using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public struct StateTick
{
    public InputTick input;
    public Vector2 position;
    public int tick;
}
public struct InputTick
{
    public int input;
    public int tick;
}



public class Player : NetworkBehaviour
{
    [SerializeField]
    private float m_Velocity;

    [SerializeField]
    private float m_Size = 1;

    private GameState m_GameState;


    // GameState peut etre nul si l'entite joueur est instanciee avant de charger MainScene
    private GameState GameState
    {
        get
        {
            if (m_GameState == null)
            {
                m_GameState = FindFirstObjectByType<GameState>();
            }
            return m_GameState;
        }
    }

    private NetworkVariable<StateTick> m_Position = new NetworkVariable<StateTick>();

    public Vector2 Position => m_Position.Value.position;

    private Queue<Vector2> m_InputQueue = new Queue<Vector2>();

    private Queue<StateTick> m_StateTickQueue = new Queue<StateTick>();


    private void Awake()
    {
        m_GameState = FindFirstObjectByType<GameState>();
    }

    private void FixedUpdate()
    {
        // Si le stun est active, rien n'est mis a jour.
        if (GameState == null || GameState.IsStunned)
        {
            return;
        }

        // Seul le serveur met à jour la position de l'entite.
        if (IsServer)
        {
            UpdatePositionServer();
        }

        // Seul le client qui possede cette entite peut envoyer ses inputs. 
        if (IsClient && IsOwner)
        {
            UpdateInputClient();
        }
    }

    private void UpdatePositionServer()
    {
        // Mise a jour de la position selon dernier input reçu, puis consommation de l'input
        if (m_InputQueue.Count > 0)
        {
            var input = m_InputQueue.Dequeue();
            m_Position.Value += input * m_Velocity * Time.deltaTime;

            // Gestion des collisions avec l'exterieur de la zone de simulation
            var size = GameState.GameSize;
            if (m_Position.Value.x - m_Size < -size.x)
            {
                m_Position.Value = new Vector2(-size.x + m_Size, m_Position.Value.y);
            }
            else if (m_Position.Value.x + m_Size > size.x)
            {
                m_Position.Value = new Vector2(size.x - m_Size, m_Position.Value.y);
            }

            if (m_Position.Value.y + m_Size > size.y)
            {
                m_Position.Value = new Vector2(m_Position.Value.x, size.y - m_Size);
            }
            else if (m_Position.Value.y - m_Size < -size.y)
            {
                m_Position.Value = new Vector2(m_Position.Value.x, -size.y + m_Size);
            }
        }
    }

    private void UpdateInputClient()
    {
        Vector2 inputDirection = new Vector2(0, 0);
        if (Input.GetKey(KeyCode.W))
        {
            inputDirection += Vector2.up;
        }
        if (Input.GetKey(KeyCode.A))
        {
            inputDirection += Vector2.left;
        }
        if (Input.GetKey(KeyCode.S))
        {
            inputDirection += Vector2.down;
        }
        if (Input.GetKey(KeyCode.D))
        {
            inputDirection += Vector2.right;
        }
        SendInputServerRpc(inputDirection.normalized);
    }


    [ServerRpc]
    private void SendInputServerRpc(Vector2 input)
    {
        // On utilise une file pour les inputs pour les cas ou on en recoit plusieurs en meme temps.
        m_InputQueue.Enqueue(input);
    }



}
