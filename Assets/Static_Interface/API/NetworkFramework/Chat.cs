﻿using Static_Interface.API.GUIFramework;
using Static_Interface.API.PlayerFramework;
using Static_Interface.API.Utils;
using UnityEngine;

namespace Static_Interface.API.NetworkFramework
{
    public class Chat : NetworkedSingletonBehaviour<Chat>
    {
        private string Message { get; set; } = "";
        private const string ChatTextFieldName = "ChatTextField";
        private bool ChatTextFieldFocused { get; set; }

        private bool _wasCursorVisible;
        private bool _chatTextFieldVisible;

        internal static void SetInstance(Chat instance)
        {
            InternalInstance = instance;
        }


        public bool ChatTextFieldVisible
        {
            get { return _chatTextFieldVisible; }
            set
            {
                if (value == _chatTextFieldVisible) return;
                _chatTextFieldVisible = value;

                //Enable cursor when chat field opens
                if (_chatTextFieldVisible)
                {
                    _wasCursorVisible = Cursor.visible;
                    Cursor.visible = true;
                }
                else
                {
                    Cursor.visible = _wasCursorVisible;
                }
            }
        }

        private ChatScrollView _chatView;
        internal ChatScrollView ChatView => _chatView;
        public void SendPlayerMessage(string text)
        {
            Channel.Send(nameof(Network_SendUserMessage), ECall.Server, text);
        }

        public void SendServerMessage(string text)
        {
            CheckServer();
            Channel.Send(nameof(Network_ReceiveMessage), ECall.All, Channel.Connection.ServerID, text);
        }

        private bool _justFocused;

        protected override void OnGUI()
        {
            base.OnGUI();
            if (InputUtil.Instance.IsInputLocked(this)) return;
            if (!ChatTextFieldVisible)
            {
                if (!Input.GetKeyDown(KeyCode.Return)) return;
                InputUtil.Instance.LockInput(this);
                ChatTextFieldVisible = true;
                FoucsChatTextField();
                _justFocused = true;
                return;
            }

            GUI.SetNextControlName(ChatTextFieldName);
            Message = GUI.TextField(new Rect(0, 200, 150, 25), Message);
            if (ChatTextFieldFocused)
            {
                GUI.FocusControl(ChatTextFieldName);
                ChatTextFieldFocused = false;
            }

            bool returnPressed = (Event.current.type == EventType.keyDown  && Event.current.character == '\n');

            if (!IsChatTextFieldFocused() || !returnPressed) return;
            if (!_justFocused)
            {
                ChatTextFieldVisible = false;
                InputUtil.Instance.UnlockInput(this);
            }
            if (_justFocused) _justFocused = false;
            if (string.IsNullOrEmpty(Message)) return;
            SendPlayerMessage(Message);
            Message = "";
        }
        
        public bool IsChatTextFieldFocused()
        {
            return GUI.GetNameOfFocusedControl() == ChatTextFieldName;
        }

        public void FoucsChatTextField()
        {
            ChatTextFieldFocused = true;
        }

        public void ClearChat()
        {
            if (IsServer())
            {
                Channel.Send(nameof(Network_ClearChatCommand), ECall.Clients);
            }
            _chatView?.Clear();
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.SERVER)]
        private void Network_SendUserMessage(Identity sender, string msg)
        {
            LogUtils.Debug(nameof(Network_SendUserMessage));
            //Todo: onchatevent
            var userName = sender.GetUser().Name;
            msg = "<color=yellow>" + userName + "</color>: " + msg;
            Channel.Send(nameof(Network_ReceiveMessage), ECall.All, sender, msg);
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.BOTH, ValidateServer = true)]
        private void Network_ReceiveMessage(Identity server, Identity sender, string formattedMessage)
        {
            PrintMessage(formattedMessage);
        }

        internal void PrintMessage(string formattedMessage)
        {
            LogUtils.Log(formattedMessage);
            if (!IsDedicatedServer())
            {
                if(_chatView == null) _chatView = new ChatScrollView("PlayerChat", Player.MainPlayer.GUI.RootView);
                _chatView.AddLine(formattedMessage);
            }
        }

        [NetworkCall(ConnectionEnd = ConnectionEnd.CLIENT, ValidateServer = true)]
        private void Network_ClearChatCommand(Identity server)
        {
            ClearChat();
        }
    }
}
