using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace UOGame
{
    /// <summary>
    /// Finishes the merge of a merged Sidekick character. Merging its parts into one skinned mesh keeps every part's
    /// copy of the skeleton: Starter_01 carries 3,176 bone slots that point at 96 transforms, and its vertices use 141
    /// of them. Every slot is a matrix skinned every frame, so the duplicate slots are collapsed onto their transform
    /// and unused ones dropped. The compacted mesh is made once per source mesh and shared by every instance.
    /// </summary>
    public static class SkinCompactor
    {
        private sealed class Compacted
        {
            public Mesh Mesh;
            /// <summary>For each new bone, the old slot whose transform it is.</summary>
            public int[] SourceSlot;
        }

        private static readonly Dictionary<Mesh, Compacted> Cache = new Dictionary<Mesh, Compacted>();

        /// <summary>Compact every skinned renderer under <paramref name="root"/> (no-op for meshes already lean).</summary>
        public static void Apply(GameObject root)
        {
            foreach (SkinnedMeshRenderer skin in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Apply(skin);
            }
        }

        public static void Apply(SkinnedMeshRenderer skin)
        {
            Mesh source = skin.sharedMesh;
            Transform[] bones = skin.bones;
            if (source == null || !source.isReadable || bones.Length <= 128)
            {
                return;
            }

            if (!Cache.TryGetValue(source, out Compacted compacted))
            {
                compacted = Build(source, bones);
                Cache[source] = compacted;
            }

            if (compacted == null)
            {
                return;
            }

            var newBones = new Transform[compacted.SourceSlot.Length];
            for (int i = 0; i < newBones.Length; i++)
            {
                newBones[i] = bones[compacted.SourceSlot[i]];
            }

            skin.sharedMesh = compacted.Mesh;
            skin.bones = newBones;
        }

        private static Compacted Build(Mesh source, Transform[] bones)
        {
            // Copies: the mesh's own arrays are only valid until the next mesh call.
            var weights = new NativeArray<BoneWeight1>(source.GetAllBoneWeights(), Allocator.Temp);
            var perVertex = new NativeArray<byte>(source.GetBonesPerVertex(), Allocator.Temp);
            Matrix4x4[] bindposes = source.bindposes;

            // New index per old slot: slots on the same transform (with the same bind pose) share one new bone.
            var remap = new int[bones.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                remap[i] = -1;
            }

            var byTransform = new Dictionary<Transform, List<int>>();
            var sourceSlot = new List<int>();
            var newWeights = new NativeArray<BoneWeight1>(weights.Length, Allocator.Temp);
            for (int w = 0; w < weights.Length; w++)
            {
                BoneWeight1 weight = weights[w];
                int slot = weight.boneIndex;
                if (remap[slot] < 0)
                {
                    Transform bone = bones[slot];
                    int found = -1;
                    if (bone != null && byTransform.TryGetValue(bone, out List<int> candidates))
                    {
                        foreach (int candidate in candidates)
                        {
                            if (SamePose(bindposes[sourceSlot[candidate]], bindposes[slot]))
                            {
                                found = candidate;
                                break;
                            }
                        }
                    }

                    if (found < 0)
                    {
                        found = sourceSlot.Count;
                        sourceSlot.Add(slot);
                        if (bone != null)
                        {
                            if (!byTransform.TryGetValue(bone, out candidates))
                            {
                                byTransform[bone] = candidates = new List<int>();
                            }

                            candidates.Add(found);
                        }
                    }

                    remap[slot] = found;
                }

                weight.boneIndex = remap[slot];
                newWeights[w] = weight;
            }

            var newPoses = new Matrix4x4[sourceSlot.Count];
            for (int i = 0; i < newPoses.Length; i++)
            {
                newPoses[i] = bindposes[sourceSlot[i]];
            }

            Mesh mesh = Object.Instantiate(source);
            mesh.name = source.name + " (compact)";
            mesh.SetBoneWeights(perVertex, newWeights);
            mesh.bindposes = newPoses;
            newWeights.Dispose();
            weights.Dispose();
            perVertex.Dispose();
            Debug.Log($"[Game] {source.name}: {bones.Length} bone slots -> {newPoses.Length}");
            return new Compacted { Mesh = mesh, SourceSlot = sourceSlot.ToArray() };
        }

        private static bool SamePose(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
            {
                if (Mathf.Abs(a[i] - b[i]) > 1e-4f)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
