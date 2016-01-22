﻿namespace Static_Interface.Internal
{
    public struct KeyState
    {
        public int KeyCode { get; set; }
        public bool IsDown { get; set; }
        public bool IsPressed { get; set; }
    }

    public class KeyStates
    {
        public const int UP = 0;
        public const int DOWN = 1;
    }
}