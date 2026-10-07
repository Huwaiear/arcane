using Robust.Shared.Prototypes;

namespace Content.Shared._Arcane.Weapons.TargetLock;

[RegisterComponent]
public sealed partial class TargetLockComponent : Component
{
    [DataField]
    public EntProtoId Action = "ActionArcaneTargetLock";

    [DataField]
    public float Range = 20f;

    [DataField]
    public float PickRadius = 3f;

    [DataField]
    public EntityUid? LockedTarget;

    [DataField]
    public EntityUid? LockedBy;

    [DataField]
    public EntityUid? PreviousEyeTarget;
}
