using System.Collections.Generic;
using UnityEngine;

namespace Riptide.Demos.DedicatedServer
{
    public class ClientInput
    {
        public bool[] Inputs = new bool[5];
        public ushort currentTick = 0;
        public Quaternion rotation;
    }

    public class Player : MonoBehaviour
    {
        public static Dictionary<ushort, Player> List { get; private set; } = new Dictionary<ushort, Player>();

        public ushort Id { get; private set; }
        public string Username { get; private set; }

        [SerializeField] public Rigidbody rb;

        private float deltaTickTime;

        private ClientInput lastReceivedInputs = new ClientInput();


        public Vector3 playerVelocity;
        public Vector3 playerAngularVelocity;

        [SerializeField] private Transform orientation;
        [SerializeField] private float moveForce;
        [SerializeField] private float maxMoveSpeed;

        [SerializeField] private Transform checkSphere;
        [SerializeField] private float checkSphereRadius;
        [SerializeField] private LayerMask groundLayerMask;
        [SerializeField] private float jumpForce;

        [SerializeField] private float gravityScale;

        //Rotation and look
        private bool grounded;


        private void Start()
        {
            Physics.simulationMode = SimulationMode.Script;

            rb = GetComponent<Rigidbody>();
            rb.isKinematic = false;
            
            deltaTickTime = Time.fixedDeltaTime;
        }

        private void HandleClientInput(ClientInput[] inputs, ushort clientID)
        {
            if (inputs.Length == 0) return;
      
            if (inputs[inputs.Length - 1].currentTick >= lastReceivedInputs.currentTick)
            {
                int start = lastReceivedInputs.currentTick > inputs[0].currentTick ? (lastReceivedInputs.currentTick - inputs[0].currentTick) : 0;

				//I need to create a new object to store all the players velocitys
				//Do the phsyics
				//then renable them with the proper 
				foreach (KeyValuePair<ushort, Player> entry in List)
				{
					ushort playerId = entry.Key;
					Player player = entry.Value;
					if (playerId != clientID)
					{
                        player.playerVelocity = player.rb.velocity;
                        player.playerAngularVelocity = player.rb.angularVelocity;
                        player.rb.isKinematic = true;
                    }	
					else
						player.rb.isKinematic = false;
				}
				for (int i = start; i < inputs.Length - 1; i++)
                {
                    PhysicsStep(inputs[i].Inputs, inputs[i].rotation);
                    Physics.Simulate(deltaTickTime);
                    SendMovement((ushort)(inputs[i].currentTick + 1));
                }

				foreach (KeyValuePair<ushort, Player> entry in List)
				{
					Player player = entry.Value;
                    ushort playerId = entry.Key;
                    player.rb.isKinematic = false;
                    if (playerId != clientID)
                    {
                        player.rb.velocity = player.playerVelocity;
                        player.rb.angularVelocity = player.playerAngularVelocity;
                    }

                    
	
				}
				lastReceivedInputs = inputs[inputs.Length - 1];
            }
        }

        private void PhysicsStep(bool[] inputs, Quaternion lookDirection)
        {
            grounded = Physics.CheckSphere(checkSphere.position, checkSphereRadius, groundLayerMask);

            Vector2 inputDirection = getDirectionFromInputs(inputs);

            if (inputs[4] && grounded)
            {
                rb.AddForce(new Vector3(0, jumpForce, 0));
            }

            rb.AddForce((lookDirection * Vector3.right) * inputDirection.x * moveForce);
            rb.AddForce((lookDirection * Vector3.forward) * inputDirection.y * moveForce);


            Vector3 vel = new Vector3(rb.velocity.x, 0, rb.velocity.z);
            rb.AddForce(-vel * (moveForce / maxMoveSpeed), ForceMode.Acceleration);

            rb.AddForce(Physics.gravity.y * Vector3.up * gravityScale, ForceMode.Acceleration);

        }

        private Vector2 getDirectionFromInputs(bool[] inputs)
        {
            Vector2 inputDirection = Vector2.zero;
            if (inputs[0])
                inputDirection.y += 1;

            if (inputs[1])
                inputDirection.y -= 1;

            if (inputs[2])
                inputDirection.x -= 1;

            if (inputs[3])
                inputDirection.x += 1;

            return inputDirection;
        }

        private void OnDestroy()
        {
            List.Remove(Id);
        }

        public static void Spawn(ushort id, string username)
        {
            Player player = Instantiate(NetworkManager.Singleton.PlayerPrefab, new Vector3(0f, 1f, 0f), Quaternion.identity).GetComponent<Player>();
            player.name = $"Player {id} ({(username == "" ? "Guest" : username)})";
            player.Id = id;
            player.Username = username;

            player.SendSpawn();
            List.Add(player.Id, player);
        }

        #region Messages
        /// <summary>Sends a player's info to the given client.</summary>
        /// <param name="toClient">The client to send the message to.</param>
        public void SendSpawn(ushort toClient)
        {
            NetworkManager.Singleton.Server.Send(GetSpawnData(Message.Create(MessageSendMode.Reliable, ServerToClientId.SpawnPlayer)), toClient);
        }
        /// <summary>Sends a player's info to all clients.</summary>
        private void SendSpawn()
        {
            NetworkManager.Singleton.Server.SendToAll(GetSpawnData(Message.Create(MessageSendMode.Reliable, ServerToClientId.SpawnPlayer)));
        }

        private Message GetSpawnData(Message message)
        {
            message.AddUShort(Id);
            message.AddString(Username);
            message.AddVector3(transform.position);
            return message;
        }

        private void SendMovement(ushort clientTick)
        {
            Message message = Message.Create(MessageSendMode.Unreliable, ServerToClientId.PlayerMovement);
            message.AddUShort(Id);
            message.AddUShort(clientTick);
            message.AddVector3(transform.position);
            message.AddVector3(transform.forward);
            message.AddVector3(rb.velocity);
            message.AddVector3(rb.angularVelocity);
            message.AddQuaternion(transform.rotation);
            NetworkManager.Singleton.Server.SendToAll(message);
        }

        [MessageHandler((ushort)ClientToServerId.PlayerName)]
        private static void PlayerName(ushort fromClientId, Message message)
        {
            Spawn(fromClientId, message.GetString());
        }

        [MessageHandler((ushort)ClientToServerId.PlayerInput)]
        private static void PlayerInput(ushort fromClientId, Message message)
        {
            Player player = List[fromClientId];


            byte inputsQuantity = message.GetByte();
            ClientInput[] inputs = new ClientInput[inputsQuantity];

            // Now we loops to get all the inputs sent by the client and store them in an array 
            for (int i = 0; i < inputsQuantity; i++)
            {
                inputs[i] = new ClientInput
                {
                    Inputs = message.GetBools(5),
                    currentTick = message.GetUShort(),
                    rotation = message.GetQuaternion()
                };
            }

            player.HandleClientInput(inputs, player.Id);
        }
        #endregion
    }
}
