using FunRabbit;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class TestCrane : MonoBehaviour
{
    [SerializeField] FunRabbit.Crane _crane;

    private bool _isMovingForward = false;
    public bool IsMovingForward
    {
        get { return _isMovingForward; }
        set 
        { 
            _isMovingForward = value; 
        }
    }

    private bool _isMovingBackward = false;
    public bool IsMovingBackward
    {
        get { return _isMovingBackward; }
        set { _isMovingBackward = value; }
    }

    private bool _isMovingLeft = false;
    public bool IsMovingLeft
    {
        get { return _isMovingLeft; }
        set { _isMovingLeft = value; }
    }

    private bool _isMovingRight = false;
    public bool IsMovingRight
    {
        get { return _isMovingRight; }
        set { _isMovingRight = value; }
    }

    private bool _isMovingUp = false;
    public bool IsMovingUp
    {
        get { return _isMovingUp; }
        set { _isMovingUp = value; }
    }

    private bool _isMovingDown = false;
    public bool IsMovingDown
    {
        get { return _isMovingDown; }
        set { _isMovingDown = value; }
    }

    void Update()
    {
        if (_crane.Status == CraneStatus.CONTROL_MOVING)
        {
            this.UpdateControlMoving();
        }
    }

    private void UpdateControlMoving()
    {
        // W
        if (Input.GetKeyDown(KeyCode.W)) _isMovingForward = true;
        if (Input.GetKeyUp(KeyCode.W)) _isMovingForward = false;

        // S
        if (Input.GetKeyDown(KeyCode.S)) _isMovingBackward = true;
        if (Input.GetKeyUp(KeyCode.S)) _isMovingBackward = false;

        // A
        if (Input.GetKeyDown(KeyCode.A)) _isMovingLeft = true;
        if (Input.GetKeyUp(KeyCode.A)) _isMovingLeft = false;

        // D
        if (Input.GetKeyDown(KeyCode.D)) _isMovingRight = true;
        if (Input.GetKeyUp(KeyCode.D)) _isMovingRight = false;

        // Keypad 1 → Down
        if (Input.GetKeyDown(KeyCode.F1)) _isMovingDown = true;
        if (Input.GetKeyUp(KeyCode.F1)) _isMovingDown = false;

        // Keypad 2 → Up
        if (Input.GetKeyDown(KeyCode.F2)) _isMovingUp = true;
        if (Input.GetKeyUp(KeyCode.F2)) _isMovingUp = false;

        // Space = Grab
        if (Input.GetKeyDown(KeyCode.Space))
        {
            _crane.StartGrabSequence();
        }
    }

    void FixedUpdate()
    {
        if (_isMovingForward) _crane.CraneTransform.MoveFront();
        if (_isMovingBackward) _crane.CraneTransform.MoveBack();
        if (_isMovingLeft) _crane.CraneTransform.MoveLeft();
        if (_isMovingRight) _crane.CraneTransform.MoveRight();

        if (_isMovingUp) _crane.CraneTransform.OnMoveUp();       // 새로 추가
        if (_isMovingDown) _crane.CraneTransform.OnMoveDown();   // 새로 추가
    }
}
