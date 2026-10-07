using System.Linq;
using System.Numerics;
using Content.Shared._Arcane.Weapons.TargetLock;
using Content.Shared.Actions;
using Content.Shared.Hands;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Projectiles;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._Arcane.Weapons;

public sealed class TargetLockSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedTransformSystem _xform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<TargetLockComponent, ArcaneTargetLockActionEvent>(OnLockAction);
        SubscribeLocalEvent<TargetLockComponent, AmmoShotEvent>(OnAmmoShot);
        SubscribeLocalEvent<TargetLockComponent, GotUnequippedHandEvent>(OnUnequipped);
    }

    private void OnLockAction(Entity<TargetLockComponent> ent, ref ArcaneTargetLockActionEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        var user = args.Performer;

        if (!TryPickTarget(ent, user, args.Target, out var target))
        {
            _popup.PopupEntity(Loc.GetString("arcane-target-lock-none"), ent.Owner, user);
            return;
        }

        SetLock(ent, user, target);
        _popup.PopupEntity(Loc.GetString("arcane-target-lock-locked"), ent.Owner, user);
    }

    private void OnUnequipped(Entity<TargetLockComponent> ent, ref GotUnequippedHandEvent args)
    {
        ReleaseLock(ent);
    }

    private void OnAmmoShot(Entity<TargetLockComponent> ent, ref AmmoShotEvent args)
    {
        if (ent.Comp.LockedTarget is not { } target || TerminatingOrDeleted(target))
        {
            ReleaseLock(ent);
            return;
        }

        var targetCoords = _xform.GetMapCoordinates(target);

        foreach (var projectile in args.FiredProjectiles)
        {
            if (!TryComp<ProjectileComponent>(projectile, out var projectileComp) ||
                !TryComp<PhysicsComponent>(projectile, out var physics))
                continue;

            var projectileCoords = _xform.GetMapCoordinates(projectile);
            if (projectileCoords.MapId != targetCoords.MapId)
                continue;

            var direction = targetCoords.Position - projectileCoords.Position;
            if (direction.LengthSquared() <= 0f)
                continue;

            direction = direction.Normalized();

            var currentMapVelocity = _physics.GetMapLinearVelocity(projectile, physics);
            var speed = currentMapVelocity.Length();
            if (speed <= 0f)
                continue;

            _physics.SetLinearVelocity(projectile,
                physics.LinearVelocity + direction * speed - currentMapVelocity,
                body: physics);

            _xform.SetWorldRotation(projectile, direction.ToWorldAngle() + projectileComp.Angle);
            projectileComp.TargetCoordinates = targetCoords.Position;

            _gun.SetTarget(projectile, target, out _);
        }

        ReleaseLock(ent);
    }

    private bool TryPickTarget(Entity<TargetLockComponent> ent,
        EntityUid user,
        EntityCoordinates coordinates,
        out EntityUid target)
    {
        target = default;

        if (!coordinates.IsValid(EntityManager))
            return false;

        var clicked = _xform.ToMapCoordinates(coordinates);
        var bestDistance = float.MaxValue;
        var found = false;

        foreach (var candidate in _lookup.GetEntitiesInRange<MobStateComponent>(clicked,
                     ent.Comp.PickRadius,
                     LookupFlags.Dynamic))
        {
            if (candidate.Owner == user || !_mobState.IsAlive(candidate.Owner, candidate.Comp))
                continue;
            if (!_interaction.InRangeUnobstructed(user, candidate.Owner, ent.Comp.Range))
                continue;

            var distance = (clicked.Position - _xform.GetMapCoordinates(candidate.Owner).Position).LengthSquared();
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            target = candidate.Owner;
            found = true;
        }

        return found;
    }

    private void SetLock(Entity<TargetLockComponent> ent, EntityUid user, EntityUid target)
    {
        ReleaseLock(ent);

        ent.Comp.LockedTarget = target;
        ent.Comp.LockedBy = user;

        if (!TryComp<EyeComponent>(user, out var eye))
            return;

        ent.Comp.PreviousEyeTarget = eye.Target;
        _eye.SetTarget(user, target, eye);
    }

    private void ReleaseLock(Entity<TargetLockComponent> ent)
    {
        if (ent.Comp.LockedBy is { } user &&
            !TerminatingOrDeleted(user) &&
            TryComp<EyeComponent>(user, out var eye))
        {
            _eye.SetTarget(user, ent.Comp.PreviousEyeTarget, eye);
        }

        ent.Comp.LockedTarget = null;
        ent.Comp.LockedBy = null;
        ent.Comp.PreviousEyeTarget = null;
    }
}
