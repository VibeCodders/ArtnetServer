using System;
using System.Collections.Concurrent;

namespace ArtnetNode.Core
{
    public class UniverseMergeManager
    {
        private readonly ConcurrentDictionary<int, DmxUniverseState> _universes = new();
        private readonly int _htpTimeoutMs;
        private readonly MergeMode _defaultMode;

        public UniverseMergeManager(int htpTimeoutMs = 1000, MergeMode defaultMode = MergeMode.Htp)
        {
            _htpTimeoutMs = htpTimeoutMs;
            _defaultMode = defaultMode;
        }

        public void RegisterUniverse(int universe)
        {
            _universes.TryAdd(universe, new DmxUniverseState(universe));
        }

        public void UpdateUniverse(int universe, byte[] dmxData, string sourceIp, byte sequence, MergeMode? mode = null)
        {
            if (!_universes.TryGetValue(universe, out var state))
            {
                state = new DmxUniverseState(universe);
                _universes[universe] = state;
            }

            state.Update(dmxData, sourceIp, sequence);
            MergeMode effectiveMode = mode ?? _defaultMode;
            ApplyMerge(state, effectiveMode);
        }

        public byte[] GetMergedDmx(int universe)
        {
            if (_universes.TryGetValue(universe, out var state))
            {
                return state.CurrentDmx;
            }
            return new byte[512];
        }

        private void ApplyMerge(DmxUniverseState state, MergeMode mode)
        {
            if (mode == MergeMode.Htp)
            {
                for (int i = 0; i < 512; i++)
                {
                    state.CurrentDmx[i] = Math.Max(state.CurrentDmx[i], state.LastReceivedDmx[i]);
                }
            }
            else
            {
                Array.Copy(state.LastReceivedDmx, state.CurrentDmx, 512);
            }
        }
    }
}
