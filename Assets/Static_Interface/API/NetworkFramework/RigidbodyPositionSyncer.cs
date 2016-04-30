﻿using Static_Interface.API.PlayerFramework;
using Static_Interface.API.Utils;
using UnityEngine;

namespace Static_Interface.API.NetworkFramework
{
    [RequireComponent(typeof(Rigidbody))]
    public class RigidbodyPositionSyncer : NetworkedBehaviour
    {
        private Rigidbody Rigidbody => GetComponent<Rigidbody>();
        private Vector3? _cachedPosition;
        private Vector3? _cachedVelocity;

        private float _lastSynchronizationTime;
        private float _syncDelay;
        private float _syncTime;
        private Vector3? _syncStartPosition;
        private Vector3? _syncEndPosition;

        protected override bool IsSyncable => true;

        public IPositionValidator PositionValidator;
        
        protected override void Update()
        {
            base.Update();
            _syncTime += Time.deltaTime;
            if (_syncStartPosition == null || _syncEndPosition == null) return;
            var vec = Vector3.Lerp(_syncStartPosition.Value, _syncEndPosition.Value, _syncTime / _syncDelay);
            Rigidbody.position = vec;

            if (vec == _syncEndPosition.Value)
            {
                _syncStartPosition = null;
                _syncEndPosition = null;
            }
        }

        protected override bool OnSync()
        {
            base.OnSync();
            if (_cachedPosition == Rigidbody.position && _cachedVelocity == Rigidbody.velocity)
            {
                // no changes, no need for updates
                return false;
            }

            _cachedPosition = Rigidbody.position;
            _cachedVelocity = Rigidbody.velocity;
            Channel.Send(nameof(Network_ReadPositionServer), ECall.Server, (object)Rigidbody.position, Rigidbody.velocity);
            return true;
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.SERVER, ValidateOwner = true)]
        protected void Network_ReadPositionServer(Identity ident, Vector3 syncPosition, Vector3 syncVelocity)
        {
            if (PositionValidator != null)
            {
                var deltaPosition = syncPosition - Rigidbody.position;
                var deltaVelocity = syncVelocity - Rigidbody.velocity;
                if (!PositionValidator.ValidatePosition(Rigidbody.transform, deltaPosition, deltaVelocity))
                {
                    Channel.Send(nameof(Network_ReadPositionClient), ECall.Owner, (object)Rigidbody.position, Rigidbody.velocity, true);
                    return;
                }
            }

            ReadPosition(syncPosition, syncVelocity, IsDedicatedServer());
            Channel.Send(nameof(Network_ReadPositionClient), ECall.NotOwner, Rigidbody.position, syncPosition, syncVelocity, false);
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.CLIENT, ValidateServer= true, MaxRadius = 1000)]
        protected void Network_ReadPositionClient(Identity ident, Vector3 syncPosition, Vector3 syncVelocity, bool force)
        {
            ReadPosition(syncPosition, syncVelocity, force);
        }

        private void ReadPosition(Vector3 syncPosition, Vector3 syncVelocity, bool snap)
        {
            if (snap)
            {
                Rigidbody.position = syncPosition;
                Rigidbody.velocity = syncVelocity;
                return;
            }
            _syncTime = 0f;
            _syncDelay = Time.time - _lastSynchronizationTime;
            _lastSynchronizationTime = Time.time;
            LastSync = TimeUtil.GetCurrentTime();
            _syncEndPosition = syncPosition + syncVelocity * _syncDelay;
            _syncStartPosition = Rigidbody.position;
        }
    }
}