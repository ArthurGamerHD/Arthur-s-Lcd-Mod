using System;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace LcdMod.Common.Terminal
{
    public sealed class TextInputLcd
    {
        public event Action<TextInputLcd> Closed;

        public IMyCubeGrid Grid { get; private set; }
        
        public IMyTextPanel Lcd { get; private set; }
        
        public long PlayerId { get; }

        readonly IMyCharacter _character;
        int _remainingTicks;

        public TextInputLcd(IMyCubeGrid grid, IMyCharacter character, long playerId, int lifetimeTicks)
        {
            Grid = grid;
            PlayerId = playerId;
            Lcd = Grid.GetCubeBlock(Vector3I.Zero).FatBlock as IMyTextPanel;
            _character = character;
            _remainingTicks = lifetimeTicks;

            if(character != null)
                character.OnMarkForClose += OnCharacterMarkedForClose;

            grid.OnMarkForClose += OnGridMarkedForClose;
        }

        // Call every tick on the client to track the character and decrement lifetime.
        public void Update()
        {
            if (Grid == null)
                return;

            Grid.WorldMatrix = _character.WorldMatrix;

            if (_remainingTicks > 0 && --_remainingTicks == 0)
                Close();
        }

        public void Close()
        {
            if (Grid == null)
                return;

            _character.OnMarkForClose -= OnCharacterMarkedForClose;
            Grid.OnMarkForClose -= OnGridMarkedForClose;

            if (!Grid.MarkedForClose)
                Grid.Close();

            Grid = null;
            Closed?.Invoke(this);
        }

        void OnCharacterMarkedForClose(IMyEntity _) => Close();

        void OnGridMarkedForClose(IMyEntity _)
        {
            if (Grid == null)
                return;

            _character.OnMarkForClose -= OnCharacterMarkedForClose;
            Grid.OnMarkForClose -= OnGridMarkedForClose;
            Grid = null;
            Closed?.Invoke(this);
        }
    }
}
