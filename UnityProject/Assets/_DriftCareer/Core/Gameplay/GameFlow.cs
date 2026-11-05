using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Gameplay
{
    public enum GameState
    {
        Boot = 0,
        MainMenu = 1,
        Garage = 2,
        Loading = 3,
        Intro = 4,
        Driving = 5,
        Paused = 6,
        LayoutEditor = 7,
        Settings = 8,
    }

    /// <summary>
    /// Explicit game state machine with an allow-list of transitions. Presentation reacts to
    /// <see cref="StateChanged"/>; illegal transitions are rejected (and testable).
    /// </summary>
    public sealed class GameFlow
    {
        private static readonly Dictionary<GameState, GameState[]> Allowed = new Dictionary<GameState, GameState[]>
        {
            { GameState.Boot, new[] { GameState.MainMenu } },
            { GameState.MainMenu, new[] { GameState.Garage, GameState.Loading, GameState.Settings } },
            { GameState.Garage, new[] { GameState.MainMenu, GameState.Loading, GameState.Driving } },
            { GameState.Settings, new[] { GameState.MainMenu, GameState.Paused } },
            { GameState.Loading, new[] { GameState.Intro, GameState.Driving, GameState.MainMenu } },
            { GameState.Intro, new[] { GameState.Driving, GameState.Paused } },
            { GameState.Driving, new[] { GameState.Paused, GameState.LayoutEditor, GameState.Loading } },
            { GameState.Paused, new[] { GameState.Driving, GameState.LayoutEditor, GameState.Settings, GameState.Garage, GameState.Loading, GameState.MainMenu } },
            { GameState.LayoutEditor, new[] { GameState.Driving, GameState.Paused } },
        };

        public GameState State { get; private set; } = GameState.Boot;
        public GameState Previous { get; private set; } = GameState.Boot;
        public event Action<GameState, GameState> StateChanged;

        /// <summary>True while the simulation should advance (not paused/editing/menus).</summary>
        public bool SimulationRunning => State == GameState.Driving || State == GameState.Intro;

        public bool CanTransition(GameState to) => Allowed.TryGetValue(State, out var list) && Array.IndexOf(list, to) >= 0;

        public bool TryTransition(GameState to)
        {
            if (!CanTransition(to)) return false;
            Previous = State;
            State = to;
            StateChanged?.Invoke(Previous, to);
            return true;
        }

        /// <summary>Returns to the previous state if allowed (e.g. Settings -> Paused).</summary>
        public bool Back() => TryTransition(Previous);
    }
}
