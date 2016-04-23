﻿using System;
using Static_Interface.API.GUIFramework;
using Static_Interface.API.NetworkFramework;
using Static_Interface.API.Utils;
using UnityEngine;

namespace Static_Interface.API.PlayerFramework
{
    public class PlayerHealth : PlayerBehaviour
    {
        private Rigidbody _rigidbody;
        private ProgressBar _healthProgressBar;
        protected override void Awake()
        {
            base.Awake();
            MaxHealth = 100;
            _health = 100;
        }

        protected override void OnPlayerLoaded()
        {
            base.OnPlayerLoaded();
            _rigidbody = GetComponent<Rigidbody>();
            if (!UseGUI()) return;
            _healthProgressBar = new ProgressBar("Health")
            {
                MaxValue = MaxHealth,
                MinValue = 0,
                Value = Health,
                Label = "Health"
            };
            Player.GUI.AddStatusProgressBar(_healthProgressBar);
        }

        public bool IsDead => Health == 0;

        private int _health;
        public int Health
        {
            get { return _health; }
            set
            {
                int newHealth = Mathf.Clamp(value, 0, MaxHealth);

                if (IsDead && newHealth > 0)
                {
                    RevivePlayer(newHealth);
                }

                ECall target = ECall.Owner;
                bool isStatusUpdate = false;
                if ((_health > 0 && newHealth == 0) || (_health == 0 && newHealth > 0))
                {
                    target = ECall.Clients;
                    isStatusUpdate = true;
                }
                _health = newHealth;
                if (_healthProgressBar != null)
                    _healthProgressBar.Value = newHealth;

                if (IsServer() && !Channel.IsOwner)
                {
                    Channel.Send(nameof(Network_SetHealth), target, MaxHealth, Health);
                }
                if (!isStatusUpdate) return;
                if (IsServer() && !Channel.IsOwner && _health == 0)
                {
                    Channel.Send(nameof(Network_Kill), target);
                }
            }
        }

        public void Kill()
        {
            Health = 0;
            OnPlayerDeath(EPlayerDeathCause.UNKNOWN);
        }

        private void OnPlayerDeath(EPlayerDeathCause reason)
        {
            if (IsServer())
            {
                Chat.Instance.SendServerMessage("<b>" + Player.User.Name + "</b> died");
            }

            _rigidbody.freezeRotation = false;
            Player.MovementController.DisableControl();
        }

        public void RevivePlayer()
        {
            RevivePlayer(MaxHealth);
        }

        public void RevivePlayer(int health)
        {
            if (health <= 0)
            {
                throw new ArgumentException("value can not be <= 0", nameof(health));
            }
            if (!IsDead)
            {
                LogUtils.LogWarning("RevivePlayer: Player is not dead!");
                return;
            }

            _health = health;

            _rigidbody.rotation = Quaternion.identity;
            _rigidbody.freezeRotation = true;
            Player.MovementController.EnableControl();

            if (IsServer() && !Channel.IsOwner)
            {
                Channel.Send(nameof(Network_Revive), ECall.Owner, _health);
            }
        }

        private int _maxHealth;

        public int MaxHealth
        {
            get { return _maxHealth; }
            set
            {
                _maxHealth = value;
                if (_healthProgressBar != null)
                    _healthProgressBar.MaxValue = MaxHealth;
            }
        }

        public void PlayerCollision(Vector3 momentum)
        {
            bool wasDead = IsDead;
            var magnitude = momentum.magnitude;
            var speed = magnitude / GetComponent<Rigidbody>().mass;
            var damage = (10 / 4) * speed;  // 100 damage at 40 m/s
            DamagePlayer((int)damage);
            if (!wasDead && IsDead)
            {
                OnPlayerDeath(EPlayerDeathCause.COLLISION);
            }
        }

        public void DamagePlayer(int damage)
        {
            Health -= damage;
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.CLIENT, ValidateServer = true)]
        private void Network_SetHealth(Identity identity, int maxhealth, int health)
        {
            MaxHealth = maxhealth;
            Health = health;
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.CLIENT, ValidateServer = true)]
        private void Network_Kill(Identity identity)
        {
            Kill();
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.CLIENT, ValidateServer = true)]
        private void Network_Revive(Identity identity, int health)
        {
            RevivePlayer(health);
        }
    }
}