using System;
using DG.Tweening;
using UnityEngine;

/// <summary>The HUD counters a reward can land on.</summary>
public enum RewardChannel { Coins = 0, XP = 1 }

/// <summary>
/// Lets a reward animation "own" a HUD counter for a moment. While a channel is held, the coin
/// counter / XP bar stop following the real balance and wait: each icon that lands advances them
/// (<see cref="Land"/>), and releasing the last hold settles them on the true value.
///
/// Begin the hold <b>before</b> granting the reward, so the counter never jumps ahead of the icons
/// flying towards it. Every hold has a timeout, so a flight that never finishes (a panel destroyed
/// mid-way, a scene change) can't freeze the HUD.
/// </summary>
public static class UIRewardHold
{
    private static readonly int[] Counts = new int[2];

    /// <summary>An icon landed on <c>channel</c> carrying <c>amount</c> of the reward.</summary>
    public static event Action<RewardChannel, int> Landed;

    /// <summary>The last hold on <c>channel</c> ended; counters should settle on the real value.</summary>
    public static event Action<RewardChannel> Released;

    public static bool IsHeld(RewardChannel channel) => Counts[(int)channel] > 0;

    /// <summary>Holds <paramref name="channel"/> until the returned hold is released or times out.</summary>
    public static Hold Begin(RewardChannel channel, float timeout = 8f) => new Hold(channel, timeout);

    /// <summary>Reports a landing. Counters move by <paramref name="amount"/> but never past the real value.</summary>
    public static void Land(RewardChannel channel, int amount) => Landed?.Invoke(channel, amount);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Survives play sessions when domain reload is off; a leftover count would freeze the HUD.
        Array.Clear(Counts, 0, Counts.Length);
        Landed = null;
        Released = null;
    }

    public sealed class Hold
    {
        public RewardChannel Channel { get; }
        public bool IsReleased { get; private set; }
        private readonly Tween _timeout;

        internal Hold(RewardChannel channel, float timeout)
        {
            Channel = channel;
            Counts[(int)channel]++;
            // OnKill also covers DOTween.KillAll on scene changes: the hold always ends.
            _timeout = DOVirtual.DelayedCall(Mathf.Max(0.1f, timeout), Release, true).OnKill(Release);
        }

        /// <summary>Ends the hold. Safe to call more than once.</summary>
        public void Release()
        {
            if (IsReleased) return;
            IsReleased = true;
            if (_timeout != null && _timeout.IsActive()) _timeout.Kill();

            int i = (int)Channel;
            Counts[i] = Mathf.Max(0, Counts[i] - 1);
            if (Counts[i] == 0) Released?.Invoke(Channel);
        }
    }
}
