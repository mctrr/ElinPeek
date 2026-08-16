using UnityEngine;

namespace ElinPeek;

/// <summary>
/// CDDA 式“窥视”核心状态机：
/// 进入窥视 → 冻结世界（gameSpeed=0）→ 把 PC 的视野原点临时重定向到拐角后的格子。
/// 敌人 AI 只看自己能否看见 PC（墙挡视线），窥视只改“玩家看到什么”，因此不会被发现。
/// </summary>
public static class PeekManager
{
    public static bool Active { get; private set; }

    private static int _prevSpeedIndex = 1;

    /// <summary>进入后的宽限帧：避免“从右键菜单点窥视”时菜单关闭的那一帧被当成异常自动退出。</summary>
    private static int _graceFrames;

    private static readonly (int, int)[] Dirs =
    {
        (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0), (-1, -1)
    };

    public static void Enter()
    {
        if (Active)
        {
            return;
        }
        if (!EClass.core.IsGameStarted || EClass.pc == null || EClass.pc.isDead)
        {
            return;
        }
        if (EClass.scene == null || EClass.scene.actionMode is not AM_Adv)
        {
            return;
        }
        Active = true;
        _graceFrames = 3;
        // 保存原速度并暂停世界：敌人回合计时 gameDelta = 0，无法行动，也就“不会发现你”。
        _prevSpeedIndex = EClass.game.gameSpeedIndex;
        EClass.game.gameSpeedIndex = 0;
        Refresh();
        Msg.Say(PeekText.T("peek.hint.enter"));
        SE.ClickOk();
    }

    public static void Exit()
    {
        if (!Active)
        {
            return;
        }
        Active = false;
        _graceFrames = 0;
        EClass.game.gameSpeedIndex = _prevSpeedIndex;
        Refresh();
        SE.CancelAction();
    }

    /// <summary>每帧调用，递减宽限帧。</summary>
    public static void TickGrace()
    {
        if (_graceFrames > 0)
        {
            _graceFrames--;
        }
    }

    /// <summary>宽限期是否已过（可以安全自动退出了）。</summary>
    public static bool GraceElapsed => _graceFrames <= 0;

    /// <summary>是否有真实图层打开（右键菜单不算）。</summary>
    public static bool HasLayerOpen => EClass.ui != null && EClass.ui.layers != null && EClass.ui.layers.Count > 0;

    /// <summary>立即按当前鼠标位置重新计算一次视野（进入/退出/每帧跟随鼠标）。</summary>
    public static void Refresh()
    {
        if (!EClass.core.IsGameStarted || EClass.pc == null || EClass.pc.isDead)
        {
            return;
        }
        EClass.pc.RecalculateFOV();
    }

    /// <summary>
    /// 根据鼠标相对 PC 的方向，计算窥视原点（墙后的拐角格子）。
    /// 返回 false 表示不重定向（鼠标在 PC 身上/不可窥视 → 保持正常视野）。
    /// </summary>
    public static bool TryGetOrigin(out int x, out int z)
    {
        x = 0;
        z = 0;
        if (!Active || !EClass.core.IsGameStarted || EClass.pc == null || EClass.pc.isDead)
        {
            return false;
        }
        if (EClass.scene == null || EClass.scene.mouseTarget == null)
        {
            return false;
        }
        Point pc = EClass.pc.pos;
        x = pc.x;
        z = pc.z;
        Point m = EClass.scene.mouseTarget.pos;
        if (m == null || !m.IsValid || !m.IsInBounds)
        {
            return false;
        }
        int dx = Mathf.Clamp(m.x - pc.x, -1, 1);
        int dz = Mathf.Clamp(m.z - pc.z, -1, 1);
        if (dx == 0 && dz == 0)
        {
            return false; // 鼠标在 PC 自己身上 → 看正常视野
        }
        int idx = IndexOf(dx, dz);
        // 1) 正前方格子（能走/能看 → 相当于探出一步）
        if (TryCandidate(pc.x + dx, pc.z + dz, out int ox, out int oz))
        {
            x = ox;
            z = oz;
            return true;
        }
        // 2) 正前方是墙 → 窥视左右两个斜角（CDDA 拐角窥视）
        (int, int) d1 = Dirs[(idx + 1) % 8];
        (int, int) d2 = Dirs[(idx + 7) % 8];
        if (TryCandidate(pc.x + d1.Item1, pc.z + d1.Item2, out ox, out oz))
        {
            x = ox;
            z = oz;
            return true;
        }
        if (TryCandidate(pc.x + d2.Item1, pc.z + d2.Item2, out ox, out oz))
        {
            x = ox;
            z = oz;
            return true;
        }
        return false;
    }

    private static int IndexOf(int dx, int dz)
    {
        for (int i = 0; i < Dirs.Length; i++)
        {
            if (Dirs[i].Item1 == dx && Dirs[i].Item2 == dz)
            {
                return i;
            }
        }
        return 0;
    }

    private static bool TryCandidate(int tx, int tz, out int x, out int z)
    {
        x = tx;
        z = tz;
        if (tx < 0 || tz < 0 || tx >= EClass._map.Size || tz >= EClass._map.Size)
        {
            return false;
        }
        Cell c = EClass._map.cells[tx, tz];
        if (c == null || c.blockSight || c.outOfBounds)
        {
            return false;
        }
        return true;
    }
}
