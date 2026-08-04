using Box3D.Interop;

namespace Box3D
{
    /// <summary>
    /// Lightweight handle to a body inside a <see cref="Box3DWorld"/>. This is a value type
    /// wrapping the native id — copy freely, and use <see cref="IsValid"/> after destruction.
    /// </summary>
    public readonly struct Box3DBody
    {
        public readonly B3BodyId Id;

        internal Box3DBody(B3BodyId id) => Id = id;

        public bool IsValid => B3Api.b3Body_IsValid(Id) != 0;

        public void Destroy() => B3Api.b3DestroyBody(Id);

        // ---------------- state ----------------

        public B3BodyType Type => B3Api.b3Body_GetType(Id);

        public void SetType(B3BodyType type) => B3Api.b3Body_SetType(Id, type);

        public B3Pos Position => B3Api.b3Body_GetPosition(Id);

        public B3Quat Rotation => B3Api.b3Body_GetRotation(Id);

        public B3WorldTransform Transform => B3Api.b3Body_GetTransform(Id);

        /// <summary>Teleport the body. For kinematic bodies moved every step, prefer <see cref="SetTargetTransform"/>.</summary>
        public void SetTransform(B3Pos position, B3Quat rotation) => B3Api.b3Body_SetTransform(Id, position, rotation);

        /// <summary>Move a kinematic body by velocity toward a target transform over one time step.</summary>
        public void SetTargetTransform(B3WorldTransform target, float timeStep, bool wake = true)
            => B3Api.b3Body_SetTargetTransform(Id, target, timeStep, wake ? (byte)1 : (byte)0);

        public B3Vec3 LinearVelocity
        {
            get => B3Api.b3Body_GetLinearVelocity(Id);
            set => B3Api.b3Body_SetLinearVelocity(Id, value);
        }

        public B3Vec3 AngularVelocity
        {
            get => B3Api.b3Body_GetAngularVelocity(Id);
            set => B3Api.b3Body_SetAngularVelocity(Id, value);
        }

        public float Mass => B3Api.b3Body_GetMass(Id);

        public bool IsAwake => B3Api.b3Body_IsAwake(Id) != 0;

        public void SetAwake(bool awake) => B3Api.b3Body_SetAwake(Id, awake ? (byte)1 : (byte)0);

        // ---------------- forces ----------------

        public void ApplyForce(B3Vec3 force, B3Pos worldPoint, bool wake = true)
            => B3Api.b3Body_ApplyForce(Id, force, worldPoint, wake ? (byte)1 : (byte)0);

        public void ApplyForceToCenter(B3Vec3 force, bool wake = true)
            => B3Api.b3Body_ApplyForceToCenter(Id, force, wake ? (byte)1 : (byte)0);

        public void ApplyLinearImpulse(B3Vec3 impulse, B3Pos worldPoint, bool wake = true)
            => B3Api.b3Body_ApplyLinearImpulse(Id, impulse, worldPoint, wake ? (byte)1 : (byte)0);

        public void ApplyLinearImpulseToCenter(B3Vec3 impulse, bool wake = true)
            => B3Api.b3Body_ApplyLinearImpulseToCenter(Id, impulse, wake ? (byte)1 : (byte)0);

        // ---------------- shape creation ----------------

        public Box3DShape AddSphere(in B3Sphere sphere)
        {
            var def = B3Api.b3DefaultShapeDef();
            return AddSphere(in sphere, in def);
        }

        public Box3DShape AddSphere(in B3Sphere sphere, in B3ShapeDef def)
            => new Box3DShape(B3Api.b3CreateSphereShape(Id, in def, in sphere));

        public Box3DShape AddCapsule(in B3Capsule capsule)
        {
            var def = B3Api.b3DefaultShapeDef();
            return AddCapsule(in capsule, in def);
        }

        public Box3DShape AddCapsule(in B3Capsule capsule, in B3ShapeDef def)
            => new Box3DShape(B3Api.b3CreateCapsuleShape(Id, in def, in capsule));

        /// <summary>Add a box hull with the given half extents.</summary>
        public Box3DShape AddBox(float halfX, float halfY, float halfZ)
        {
            var def = B3Api.b3DefaultShapeDef();
            return AddBox(halfX, halfY, halfZ, in def);
        }

        public Box3DShape AddBox(float halfX, float halfY, float halfZ, in B3ShapeDef def)
        {
            var hull = B3Api.b3MakeBoxHull(halfX, halfY, halfZ);
            return new Box3DShape(B3Api.b3CreateHullShape(Id, in def, in hull));
        }

        /// <summary>Add a triangle-mesh shape. The mesh data is shared; keep the <see cref="Box3DMesh"/> alive.</summary>
        public Box3DShape AddMesh(Box3DMesh mesh, B3Vec3 scale)
        {
            var def = B3Api.b3DefaultShapeDef();
            return AddMesh(mesh, scale, in def);
        }

        public Box3DShape AddMesh(Box3DMesh mesh, B3Vec3 scale, in B3ShapeDef def)
            => new Box3DShape(B3Api.b3CreateMeshShape(Id, in def, mesh.Handle, scale));
    }

    /// <summary>Lightweight handle to a shape attached to a body.</summary>
    public readonly struct Box3DShape
    {
        public readonly B3ShapeId Id;

        internal Box3DShape(B3ShapeId id) => Id = id;

        public bool IsValid => B3Api.b3Shape_IsValid(Id) != 0;

        public void Destroy(bool updateBodyMass = true)
            => B3Api.b3DestroyShape(Id, updateBodyMass ? (byte)1 : (byte)0);
    }
}
