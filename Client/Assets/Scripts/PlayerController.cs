using UnityEngine;

namespace Riptide.Demos.DedicatedClient
{
    public class ClientInput
    {
        public bool[] Inputs = new bool[5];
        public ushort currentTick = 0;
        public Quaternion rotation;
    }

    public class SimulationState
    {
        public Vector3 position;
        public Quaternion rotation;
        public ushort currentTick = 0;
    }

    public class ServerSimulationState
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 velocity;
        public Vector3 angularVelocity;
        public ushort currentTick = 0;
    }

    public class PlayerController : MonoBehaviour
    {
        private float deltaTickTime;
        public ushort cTick;
        public const int CacheSize = 1024;

        private ClientInput[] inputCache;
        private SimulationState[] clientStateCache;

        private Rigidbody rb;
        public Player playerScript;

        private int lastCorrectedFrame;

        private Vector3 clientPosError;
        private Quaternion clientRotError;


        [SerializeField] private Transform mainCamera;
        [SerializeField] private Transform orientation;
        [SerializeField] private float moveForce;
        [SerializeField] private float maxMoveSpeed;

        [SerializeField] private Transform checkSphere;
        [SerializeField] private float checkSphereRadius;
        [SerializeField] private LayerMask groundLayerMask;
        [SerializeField] private float jumpForce;

        [SerializeField] private float gravityScale;

        //Rotation and look
        private float xRotation;
        private float sensitivity = 50f;
        private float sensMultiplier = 1f;
        public bool grounded;


        private void Awake()
        {
            playerScript = GetComponent<Player>();
        }

        private void Start()
        {
            Physics.simulationMode = SimulationMode.Script;

            deltaTickTime = Time.fixedDeltaTime;

            rb = GetComponent<Rigidbody>();
            
            rb.isKinematic = false;

            
            inputCache = new ClientInput[CacheSize];
            clientStateCache = new SimulationState[CacheSize];

            clientPosError = Vector3.zero;
            clientRotError = Quaternion.identity;

            cTick = 0;

            Cursor.visible = false;
            Cursor.lockState = CursorLockMode.Locked;
        }


        private float desiredX;
        private void Look()
        {
            float mouseX = Input.GetAxis("Mouse X") * sensitivity * Time.fixedDeltaTime * sensMultiplier;
            float mouseY = Input.GetAxis("Mouse Y") * sensitivity * Time.fixedDeltaTime * sensMultiplier;

            //Find current look rotation
            Vector3 rot = mainCamera.transform.localRotation.eulerAngles;
            desiredX = rot.y + mouseX;

            //Rotate, and also make sure we dont over- or under-rotate.
            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            //Perform the rotations
            mainCamera.transform.localRotation = Quaternion.Euler(xRotation, desiredX, 0);
            orientation.transform.localRotation = Quaternion.Euler(0, desiredX, 0);
        }

        private void Update()
        {
            Look();

            int cacheIndex = cTick % CacheSize;
            inputCache[cacheIndex] = GetInput();
        }

		private void FixedUpdate()
		{
            int cacheIndex = cTick % CacheSize;

            inputCache[cacheIndex] = GetInput();
            clientStateCache[cacheIndex] = CurrentSimulationState(rb);

            PhysicsStep(inputCache[cacheIndex].Inputs, orientation.transform.rotation);
            Physics.Simulate(deltaTickTime);
            SendInput();

            ++cTick;

            if (playerScript.serverSimulationState != null) Reconciliate();
        }

		private void Reconciliate()
        {
            if (playerScript.serverSimulationState.currentTick <= lastCorrectedFrame) return;

            
            ServerSimulationState serverSimulationState = playerScript.serverSimulationState;

            uint cacheIndex = (uint)serverSimulationState.currentTick % CacheSize;
            SimulationState cachedSimulationState = clientStateCache[cacheIndex];


            Vector3 positionError = serverSimulationState.position - cachedSimulationState.position;
            //float rotationError = 1.0f - Quaternion.Dot(serverSimulationState.rotation, cachedSimulationState.rotation);

            
            if (positionError.sqrMagnitude > 0.0000001f)
            {
                Debug.Log("Correcting for error at tick " + serverSimulationState.currentTick + " (rewinding " + (cTick - cachedSimulationState.currentTick) + " ticks)");
                // capture the current predicted pos for smoothing
                Vector3 prevPos = rb.position + clientPosError;
                Quaternion prevRot = orientation.rotation * clientRotError;

                // rewind & replay
                rb.position = serverSimulationState.position;
                orientation.rotation = serverSimulationState.rotation;
                rb.velocity = serverSimulationState.velocity;
                rb.angularVelocity = serverSimulationState.angularVelocity;

                uint rewindTickNumber = serverSimulationState.currentTick;
                while (rewindTickNumber < cTick)
                {
                    cacheIndex = rewindTickNumber % CacheSize;

                    clientStateCache[cacheIndex] = CurrentSimulationState(rb);

                    PhysicsStep(inputCache[cacheIndex].Inputs, inputCache[cacheIndex].rotation);
                    Physics.Simulate(deltaTickTime);

                    ++rewindTickNumber;
                }

                // if more than 2ms apart, just snap
                if ((prevPos - rb.position).sqrMagnitude >= 4.0f)
                {
                    clientPosError = Vector3.zero;
                    clientRotError = Quaternion.identity;
                }
                else
                {
                    clientPosError = prevPos - rb.position;
                    clientRotError = Quaternion.Inverse(orientation.rotation) * prevRot;
                }
            }
            lastCorrectedFrame = playerScript.serverSimulationState.currentTick;
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

        private SimulationState CurrentSimulationState(Rigidbody rb)
        {
            return new SimulationState
            {
                position = rb.position,
                rotation = orientation.transform.rotation,
                currentTick = cTick
            };
        }

        private ClientInput GetInput()
        {
            bool[] tempInputs = new bool[5];
            if (Input.GetKey(KeyCode.W))
                tempInputs[0] = true;

            if (Input.GetKey(KeyCode.S))
                tempInputs[1] = true;

            if (Input.GetKey(KeyCode.A))
                tempInputs[2] = true;

            if (Input.GetKey(KeyCode.D))
                tempInputs[3] = true;

            if (Input.GetKey(KeyCode.Space))
                tempInputs[4] = true;

            return new ClientInput
            {
                Inputs = tempInputs,
                rotation = orientation.transform.rotation,
                currentTick = cTick
            };
        }

        #region Messages
        private void SendInput()
        {
            Message message = Message.Create(MessageSendMode.Unreliable, ClientToServerId.PlayerInput);

            message.AddByte((byte)(cTick - playerScript.serverSimulationState.currentTick));

            for (int i = playerScript.serverSimulationState.currentTick; i < cTick; i++)
            {
                message.AddBools(inputCache[i % CacheSize].Inputs, false);
                message.AddUShort(inputCache[i % CacheSize].currentTick);
                message.AddQuaternion(inputCache[i % CacheSize].rotation);
            }
            NetworkManager.Singleton.Client.Send(message);
        }
        #endregion
    }
}
