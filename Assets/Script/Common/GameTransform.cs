using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace FunRabbit
{
    public class GameTransform<T> : InstanceSetter<T> where T : MonoBehaviour
    {
        public class ApplyType
        {
            public static int TIME = 0;                 // 시간 동안 적용
            public static int IMMEDIATELY = 1;          // 즉시 적용.
            public static int SLERP = 2;                // slerp 적용.
        }

        protected Transform _cache_move_transform;
        protected Transform _cache_rotation_transform;
        protected Transform _cache_scale_transform;

        protected bool _isApplyPosition = false;
        protected bool _isApplyRotation = false;
        protected bool _isApplyScale = false;

        protected Vector3 _position;
        protected Vector3 _local_position;
        protected Vector3 _scale;
        protected Quaternion _local_rotation;
        protected Quaternion _rotation;

        private int _moveType = ApplyType.IMMEDIATELY;
        private int _rotationType = ApplyType.IMMEDIATELY;
        private int _scaleType = ApplyType.IMMEDIATELY;

        private float _rotationSpeed;
        private float _curRotationTime;
        private float _targetRotationTime;
        private Quaternion _fromRotation, _targetRotation;
        private Vector3 _originEulerRotation;

        private Vector3 _targetPosition;

        private float _curScaleTime;
        private float _targetScaleTime;
        private Vector3 _fromScale, _targetScale;

        protected Vector3[] _statusPosition = new Vector3[20];
        protected int _status = 0;

        public int Status { get { return _status; } }

        public Vector3 OriginEulerRotation
        {
            get { return _originEulerRotation; }
            set { _originEulerRotation = value; }
        }

        public void SetLocalPosition(Vector3 value)
        {
            _local_position = value;
            transform.localPosition = value;
        }

        public Vector3 LocalPosition
        {
            get { return _local_position; }
        }

        public virtual Vector3 Position
        {
            get { return _position; }
        }

        public Quaternion LocalRotation
        {
            get { return _local_rotation; }
        }

        public Quaternion Rotation
        {
            get { return _rotation; }
        }

        public Vector3 Scale
        {
            get { return _scale; }
        }

        private void Start()
        {

        }

        public virtual void SetStatus(int status)
        {
            if (_status == status)
                return;

            _status = status;

            // 현제 위치를 설정 해줍니다.
            SetPosition(_position, status);
        }

        public void SetTargetTransform(Transform targetTransform)
        {
            _cache_move_transform = targetTransform;
            _cache_rotation_transform = targetTransform;
            _cache_scale_transform = targetTransform;
        }

        public void SetMoveTransform(Transform targetTransform)
        {
            _cache_move_transform = targetTransform;
        }

        public void SetRotationTransform(Transform targetTransform)
        {
            _cache_rotation_transform = targetTransform;
        }

        public void SetScaleTransform(Transform targetTransform)
        {
            _cache_scale_transform = targetTransform;
        }

        #region POSITION
        public Vector3 GetStatusPosition(int state)
        {
            return _statusPosition[state];
        }

        public virtual void SetPosition(Vector3 value, int status = 0, bool applyImmediate = true)
        {
            _position = value;
            if (applyImmediate)
                _cache_move_transform.position = value;
            else
                _isApplyPosition = true;
            _moveType = ApplyType.IMMEDIATELY;

            _statusPosition[status] = value;
        }

        public void SetPositionSlerp(Vector3 value, int status = 0)
        {
            _targetPosition = value;
            _moveType = ApplyType.SLERP;
        }

        public virtual void AddPosition(Vector3 value, int status = 0, bool applyImmediate = true)
        {
            _position += value;

            if (applyImmediate)
                _cache_move_transform.position = _position;
            else
                _isApplyPosition = true;

            _statusPosition[status] += value;
            _moveType = ApplyType.IMMEDIATELY;
        }
        #endregion

        #region SCALE
        public void SetScale(Vector3 value, bool applyImmediate = false)
        {
            _scaleType = ApplyType.IMMEDIATELY;
            _SetScale(value, applyImmediate);
        }

        public void SetScaleTime(Vector3 value, float time)
        {
            _scaleType = ApplyType.TIME;
            _curScaleTime = 0.0f;
            _targetScaleTime = time;
            _fromScale = _scale;
            _targetScale = value;
        }

        private void _SetScale(Vector3 value, bool applyImmediate = false)
        {
            _scale = value;
            if (applyImmediate)
            {
                _cache_scale_transform.localScale = value;
            }
            else
            {
                _isApplyScale = true;
            }
        }

        private void UpdateScale(float deltaTime)
        {
            if (_scaleType == ApplyType.TIME)
            {
                _curScaleTime += deltaTime;
                float ratio = Mathf.Min(1.0f, _curScaleTime / _targetScaleTime);
                _SetScale(Vector3.Lerp(_fromScale, _targetScale, ratio));

                if (ratio >= 1)
                    _scaleType = ApplyType.IMMEDIATELY;
            }
        }

        #endregion

        #region ROTATION

        private void SetRotationValues(Quaternion targetRotation, float targetRotationTime, float rotationSpeed, int rotationType)
        {
            _fromRotation = Rotation;
            _targetRotation = targetRotation;
            _targetRotationTime = targetRotationTime;
            _rotationSpeed = rotationSpeed;
            _rotationType = rotationType;
        }

        private void _SetRotation(Quaternion rotation, bool applyImmediate = false)
        {
            _rotation = rotation;
            if (applyImmediate)
                _cache_rotation_transform.rotation = rotation;
            else
                _isApplyRotation = true;
        }

        public void SetLocalRotation(Quaternion rotation)
        {
            _local_rotation = rotation;
            _cache_rotation_transform.localRotation = rotation;
        }

        public virtual void SetRotation(Quaternion rotation, bool applyImmediate = true)
        {
            _SetRotation(rotation, applyImmediate);
            _rotationType = ApplyType.IMMEDIATELY;
        }

        public void SetRotationDegree(float degree, bool applyImmediate = false)
        {
            _SetRotation(Quaternion.LookRotation(Quaternion.Euler(new Vector3(0, degree, 0)) * Vector3.forward), applyImmediate);
            _rotationType = ApplyType.IMMEDIATELY;
        }

        public void SetRotationDir(Vector3 dir)
        {
            _SetRotation(Quaternion.LookRotation(dir));
            _rotationType = ApplyType.IMMEDIATELY;
        }

        public void SetRotationSlerp(Quaternion rotation, float speed = 1.0f)
        {
            SetRotationValues(rotation, 0, speed, ApplyType.SLERP);
        }

        public void SetRotationDirSlerp(Vector3 rotationDir, float speed)
        {
            if (rotationDir.sqrMagnitude == 0.0f)
                return;

            rotationDir.y = 0.0f;
            SetRotationValues(Quaternion.LookRotation(rotationDir), 0, speed, ApplyType.SLERP);
        }

        public void SetRotationDegreeSlerp(float degree, float speed)
        {
            SetRotationDirSlerp(Quaternion.Euler(new Vector3(0, degree, 0)) * Vector3.forward, speed);
        }

        public void SetRoatationToTargetSlerp(Vector3 from, Vector3 target, float speed = 1.0f)
        {
            Vector3 targetDir = target - from;
            if (targetDir.magnitude > 0.1f)
                SetRotationDirSlerp(targetDir, speed);
        }

        public void RevertLerpTargetRotation()
        {
            _SetRotation(_targetRotation);
            _rotationType = ApplyType.IMMEDIATELY;
        }

        private void UpdateMove(float deltaTime)
        {
            if (_moveType == ApplyType.TIME)
            {
                SetPosition(Vector3.Slerp(_cache_move_transform.position, _targetPosition, deltaTime));
            }
        }

        private void UpdateRotation(float deltaTime)
        {
            if (_rotationType == ApplyType.TIME)
            {
                _curRotationTime += deltaTime;
                float ratio = Mathf.Min(1.0f, _curRotationTime / _targetRotationTime);
                _SetRotation(Quaternion.Lerp(_fromRotation, _targetRotation, ratio));

                if (ratio >= 1)
                    _rotationType = ApplyType.IMMEDIATELY;
            }
            else if (_rotationType == ApplyType.SLERP)
            {
                deltaTime = _rotationSpeed * 10.0f * deltaTime;
                _SetRotation(Quaternion.Slerp(_rotation, _targetRotation, deltaTime));
            }
        }
        #endregion

        public void UpdateManual(float deltaTime)
        {
            UpdateMove(deltaTime);
            UpdateRotation(deltaTime);
            UpdateScale(deltaTime);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            if (ReferenceEquals(_cache_move_transform, null) == false)
            {
                if (_isApplyPosition)
                {
                    _cache_move_transform.position = _position;
                }

                if (_isApplyRotation)
                {
                    _cache_rotation_transform.rotation = _rotation;
                }

                if (_isApplyScale)
                {
                    _cache_scale_transform.localScale = _scale;
                }

                _isApplyScale = false;
                _isApplyRotation = false;
                _isApplyPosition = false;
            }
        }
    }
}
