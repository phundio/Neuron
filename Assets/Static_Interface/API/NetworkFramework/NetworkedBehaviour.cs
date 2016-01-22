﻿using UnityEngine;

namespace Static_Interface.Internal
{
    public abstract class NetworkedBehaviour : MonoBehaviour
    {
        public Channel Channel { get; protected set; }

        protected virtual void Awake()
        {
            Channel = GetComponent<Channel>();
            if (Channel == null)
            {
                Channel = gameObject.AddComponent<Channel>();
            }
        }
    }
}