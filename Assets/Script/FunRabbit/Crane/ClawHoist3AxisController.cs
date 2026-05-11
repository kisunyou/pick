using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 무한 범위(충돌체로만 제한) 허브 이동 + 입력 + 자동 그랩 시퀀스
/// - 허브(Rigidbody + ConfigurableJoint, connectedBody=상단 고정바디)
/// - 집게 날개(HingeJoint들) : useSpring=true, axis/anchor 정확히
/// - 이동 범위는 min/max 없이 충돌체가 한계가 됨
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public class ClawHoist3AxisController : MonoBehaviour
{
    [Header("Joint (허브)")]
    [SerializeField] private ConfigurableJoint hoistJoint;

    [Header("Drive")]
    [SerializeField] private float positionSpring = 15000f;
    [SerializeField] private float positionDamper = 200f;
    [SerializeField] private float maximumForce = Mathf.Infinity;
    [SerializeField] private float followSpeed = 5f;

    [Header("Projection")]
    [SerializeField] private JointProjectionMode projectionMode = JointProjectionMode.PositionAndRotation;
    [SerializeField] private float projectionDistance = 0.01f;
    [SerializeField] private float projectionAngle = 1f;

    [Header("Rigidbody (허브)")]
    [SerializeField] private bool useGravity = true;
    [SerializeField] private CollisionDetectionMode collisionMode = CollisionDetectionMode.ContinuousDynamic;
    [SerializeField] private int solverIterations = 12;
    [SerializeField] private int solverVelocityIterations = 12;

    [Header("이동 속도")]
    [SerializeField] private float planarSpeed = 1.2f; // WASD
    [SerializeField] private float verticalSpeed = 1.0f; // Q/E

    [Header("키 매핑")]
    [SerializeField] private KeyCode keyForward = KeyCode.W;
    [SerializeField] private KeyCode keyBackward = KeyCode.S;
    [SerializeField] private KeyCode keyLeft = KeyCode.A;
    [SerializeField] private KeyCode keyRight = KeyCode.D;
    [SerializeField] private KeyCode keyUp = KeyCode.E;
    [SerializeField] private KeyCode keyDown = KeyCode.Q;
    [SerializeField] private KeyCode keyGrab = KeyCode.Space;

    [Header("집게 Hinge")]
    [SerializeField] private HingeJoint[] clawHinges;
    [SerializeField] private float clawOpenAngle = 25f;
    [SerializeField] private float clawCloseAngle = -5f;
    [SerializeField] private float clawSpring = 800f;
    [SerializeField] private float clawDamper = 20f;

    [Header("Drop/Contact/Lift")]
    [SerializeField] private float dropSpringScale = 0.25f;
    [SerializeField] private float groundCheckDistance = 0.15f;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField] private float groundContactHoldSeconds = 2f;
    [SerializeField] private float groundHover = 0.01f;
    [SerializeField] private float groundHoldY_Damper = 600f;
    [SerializeField] private float topArriveTolerance = 0.01f;

    [Header("이벤트")]
    public UnityEvent OnGrabStart;
    public UnityEvent OnClawClosed;
    public UnityEvent OnClawOpened;
    public UnityEvent OnGrabEnd;

    // 내부 상태
    private Rigidbody _rb;
    private Vector3 _desiredLocal;
    private Vector3 _pendingVelocity;
    private bool _initialized;

    private bool isMovingForward, isMovingBackward, isMovingLeft, isMovingRight, isMovingUp, isMovingDown;

    private enum GrabState { Idle, Dropping, GroundHold, Lifting, Cooldown }
    private GrabState _state = GrabState.Idle;
    private bool _inputLocked = false;

    private JointDrive _driveSavedX, _driveSavedY, _driveSavedZ;
    private JointDrive _savedY;
    private bool _groundSnapped = false;

    private void Reset() => hoistJoint = GetComponent<ConfigurableJoint>();

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        ValidateAndSetupJoint();
        _desiredLocal = hoistJoint ? hoistJoint.targetPosition : Vector3.zero;
        _initialized = true;
        ApplyClawSpring(clawOpenAngle);
    }

    private void OnValidate()
    {
        if (Application.isPlaying) return;
        if (!hoistJoint) hoistJoint = GetComponent<ConfigurableJoint>();
        var rb = GetComponent<Rigidbody>();
        if (rb)
        {
            rb.useGravity = useGravity;
            rb.collisionDetectionMode = collisionMode;
            rb.solverIterations = Mathf.Max(1, solverIterations);
            rb.solverVelocityIterations = Mathf.Max(1, solverVelocityIterations);
        }
        if (hoistJoint) ApplyJointCommonSettings();
    }

    private void Update()
    {
        if (!_inputLocked) UpdateControlMoving();
        if (Input.GetKeyDown(keyGrab) && _state == GrabState.Idle)
            StartCoroutine(GrabSequence());
    }

    private void FixedUpdate()
    {
        if (!_initialized || hoistJoint == null) return;

        if (!_inputLocked)
        {
            if (isMovingForward) MoveFront();
            if (isMovingBackward) MoveBack();
            if (isMovingLeft) MoveLeft();
            if (isMovingRight) MoveRight();
            if (isMovingUp) MoveUp();
            if (isMovingDown) MoveDown();
        }

        if (_pendingVelocity.sqrMagnitude > 0f)
        {
            ApplyWorldVelocity(_pendingVelocity);
            _pendingVelocity = Vector3.zero;
        }

        Vector3 tp = hoistJoint.targetPosition;
        tp = Vector3.Lerp(tp, _desiredLocal, followSpeed * Time.fixedDeltaTime);
        hoistJoint.targetPosition = tp;
    }

    // ========= 입력 처리 =========
    private void UpdateControlMoving()
    {
        if (Input.GetKeyDown(keyForward)) isMovingForward = true;
        if (Input.GetKeyUp(keyForward)) isMovingForward = false;
        if (Input.GetKeyDown(keyBackward)) isMovingBackward = true;
        if (Input.GetKeyUp(keyBackward)) isMovingBackward = false;
        if (Input.GetKeyDown(keyLeft)) isMovingLeft = true;
        if (Input.GetKeyUp(keyLeft)) isMovingLeft = false;
        if (Input.GetKeyDown(keyRight)) isMovingRight = true;
        if (Input.GetKeyUp(keyRight)) isMovingRight = false;
        if (Input.GetKeyDown(keyUp)) isMovingUp = true;
        if (Input.GetKeyUp(keyUp)) isMovingUp = false;
        if (Input.GetKeyDown(keyDown)) isMovingDown = true;
        if (Input.GetKeyUp(keyDown)) isMovingDown = false;
    }

    // ========= 이동 =========
    public void Move(Vector3 worldVelocity) => _pendingVelocity += worldVelocity;

    public void MoveFront() => Move(AnchorRef().forward * planarSpeed);
    public void MoveBack() => Move(-AnchorRef().forward * planarSpeed);
    public void MoveRight() => Move(AnchorRef().right * planarSpeed);
    public void MoveLeft() => Move(-AnchorRef().right * planarSpeed);
    public void MoveUp() => Move(AnchorRef().up * verticalSpeed);
    public void MoveDown() => Move(-AnchorRef().up * verticalSpeed);

    private void ApplyWorldVelocity(Vector3 worldVelocity)
    {
        Vector3 worldDelta = worldVelocity * Time.fixedDeltaTime;
        Transform a = AnchorRef();
        float dx = Vector3.Dot(worldDelta, a.right);
        float dy = Vector3.Dot(worldDelta, a.up);
        float dz = Vector3.Dot(worldDelta, a.forward);
        _desiredLocal += new Vector3(dx, dy, dz); // 무한 범위 (클램프 없음)
    }

    // ========= Grab 시퀀스 =========
    private IEnumerator GrabSequence()
    {
        OnGrabStart?.Invoke();
        _inputLocked = true;
        _state = GrabState.Dropping;

        SaveDrives();
        SetDriveScale(dropSpringScale);
        _rb.useGravity = true;

        // 강하게 아래로 유도 (충돌체가 한계)
        SetLocalTargetY(LocalY() - 10f);

        _state = GrabState.GroundHold;
        float t = 0f;
        while (true)
        {
            if (TryGroundHit(out var hit))
            {
                if (!_groundSnapped) SnapToGround(hit);
                t += Time.fixedDeltaTime;
            }
            else t = 0f;

            if (t >= groundContactHoldSeconds) break;
            yield return new WaitForFixedUpdate();
        }

        CloseClaws();
        OnClawClosed?.Invoke();
        yield return new WaitForSeconds(0.2f);

        _state = GrabState.Lifting;
        RestoreYDriveDamper();
        RestoreDrives();
        _rb.useGravity = false;
        SetLocalTargetY(LocalY() + 10f);

        while (_rb.linearVelocity.sqrMagnitude > 0.0025f)
            yield return new WaitForFixedUpdate();

        OpenClaws();
        OnClawOpened?.Invoke();

        _state = GrabState.Cooldown;
        yield return new WaitForSeconds(1f);

        _state = GrabState.Idle;
        _inputLocked = false;
        OnGrabEnd?.Invoke();
    }

    // ========= 접지 안정화 =========
    private bool TryGroundHit(out RaycastHit hit)
    {
        Vector3 origin = transform.position;
        Vector3 dir = -AnchorRef().up;
        return Physics.Raycast(origin, dir, out hit, groundCheckDistance, groundMask);
    }

    private void SnapToGround(RaycastHit hit)
    {
        Transform a = AnchorRef();
        float targetY = a.InverseTransformPoint(hit.point + a.up * groundHover).y;
        _desiredLocal.y = targetY;

        var v = _rb.linearVelocity;
        v -= Vector3.Project(v, a.up);
        _rb.linearVelocity = v;

        _rb.useGravity = false;
        _savedY = hoistJoint.yDrive;
        var yd = hoistJoint.yDrive;
        yd.positionDamper = groundHoldY_Damper;
        hoistJoint.yDrive = yd;

        _groundSnapped = true;
    }

    private void RestoreYDriveDamper()
    {
        if (_groundSnapped)
        {
            hoistJoint.yDrive = _savedY;
            _groundSnapped = false;
        }
    }

    // ========= 집게 제어 =========
    private void ApplyClawSpring(float targetAngle)
    {
        if (clawHinges == null) return;
        foreach (var h in clawHinges)
        {
            if (!h) continue;
            h.useSpring = true;
            var sp = h.spring;
            sp.spring = clawSpring;
            sp.damper = clawDamper;
            sp.targetPosition = targetAngle;
            h.spring = sp;
        }
    }
    private void OpenClaws() => ApplyClawSpring(clawOpenAngle);
    private void CloseClaws() => ApplyClawSpring(clawCloseAngle);

    // ========= 공통 유틸 =========
    private void SaveDrives()
    {
        _driveSavedX = hoistJoint.xDrive;
        _driveSavedY = hoistJoint.yDrive;
        _driveSavedZ = hoistJoint.zDrive;
    }
    private void RestoreDrives()
    {
        hoistJoint.xDrive = _driveSavedX;
        hoistJoint.yDrive = _driveSavedY;
        hoistJoint.zDrive = _driveSavedZ;
    }
    private void SetDriveScale(float scale)
    {
        hoistJoint.xDrive = ScaledDrive(_driveSavedX, scale);
        hoistJoint.yDrive = ScaledDrive(_driveSavedY, scale);
        hoistJoint.zDrive = ScaledDrive(_driveSavedZ, scale);
    }
    private static JointDrive ScaledDrive(JointDrive src, float scale)
    {
        return new JointDrive
        {
            positionSpring = src.positionSpring * scale,
            positionDamper = src.positionDamper,
            maximumForce = src.maximumForce
        };
    }
    private float LocalY()
    {
        return WorldToJointLocal(transform.position, AnchorRef()).y;
    }
    private void SetLocalTargetY(float y)
    {
        _desiredLocal.y = y;
    }

    private void ValidateAndSetupJoint()
    {
        if (!hoistJoint)
        {
            hoistJoint = GetComponent<ConfigurableJoint>();
            if (!hoistJoint) { Debug.LogError("ConfigurableJoint 필요"); enabled = false; return; }
        }
        if (!_rb)
        {
            _rb = GetComponent<Rigidbody>();
            if (!_rb) { Debug.LogError("Rigidbody 필요"); enabled = false; return; }
        }

        _rb.useGravity = useGravity;
        _rb.collisionDetectionMode = collisionMode;
        _rb.solverIterations = Mathf.Max(1, solverIterations);
        _rb.solverVelocityIterations = Mathf.Max(1, solverVelocityIterations);

        ApplyJointCommonSettings();

        _driveSavedX = hoistJoint.xDrive;
        _driveSavedY = hoistJoint.yDrive;
        _driveSavedZ = hoistJoint.zDrive;
    }

    private void ApplyJointCommonSettings()
    {
        hoistJoint.xMotion = ConfigurableJointMotion.Free;
        hoistJoint.yMotion = ConfigurableJointMotion.Free;
        hoistJoint.zMotion = ConfigurableJointMotion.Free;

        hoistJoint.angularXMotion = ConfigurableJointMotion.Locked;
        hoistJoint.angularYMotion = ConfigurableJointMotion.Locked;
        hoistJoint.angularZMotion = ConfigurableJointMotion.Locked;

        hoistJoint.projectionMode = projectionMode;
        hoistJoint.projectionDistance = projectionDistance;
        hoistJoint.projectionAngle = projectionAngle;

        var d = new JointDrive
        {
            positionSpring = positionSpring,
            positionDamper = positionDamper,
            maximumForce = maximumForce
        };
        hoistJoint.xDrive = d;
        hoistJoint.yDrive = d;
        hoistJoint.zDrive = d;
    }

    private Transform AnchorRef()
    {
        if (hoistJoint.connectedBody) return hoistJoint.connectedBody.transform;
        if (hoistJoint.transform.parent) return hoistJoint.transform.parent;
        return hoistJoint.transform;
    }

    private static Vector3 WorldToJointLocal(Vector3 worldPos, Transform jointSpace)
    {
        return jointSpace.InverseTransformPoint(worldPos);
    }

#if UNITY_EDITOR
    [Header("Gizmos")]
    [SerializeField] private bool drawGizmos = true;
    [SerializeField] private Color targetColor = new Color(1f, 0.6f, 0f, 0.8f);
    [SerializeField] private float targetSphereRadius = 0.03f;

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos || hoistJoint == null) return;
        Transform a = AnchorRef();
        Vector3 targetWorld = a.TransformPoint(Application.isPlaying ? _desiredLocal : hoistJoint.targetPosition);
        Gizmos.color = targetColor;
        Gizmos.DrawSphere(targetWorld, targetSphereRadius);
    }
#endif
}
