// InfluenceComputeBackend.cs — the influence field as a Metal compute kernel.
//
// The dispatch is asynchronous: results come back through AsyncGPUReadback a
// frame or two later and the AI uses the latest completed field. At 6 thinks
// per second that latency is well inside the AI's own reaction delay, and it
// keeps the render thread from ever stalling on a synchronous readback.
using System.Diagnostics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarForge.AI
{
    public sealed class InfluenceComputeBackend : IInfluenceBackend, System.IDisposable
    {
        readonly ComputeShader shader;
        readonly int kernel;
        ComputeBuffer unitBuffer;
        ComputeBuffer fieldBuffer;
        float[] latest;
        bool pending, hasLatest;
        readonly Stopwatch sw = new Stopwatch();

        public string Name => "GPU compute (" + SystemInfo.graphicsDeviceType + ")";
        public float LastMs { get; private set; }

        InfluenceComputeBackend(ComputeShader cs)
        {
            shader = cs;
            kernel = cs.FindKernel("CSMain");
        }

        public static InfluenceComputeBackend TryCreate()
        {
            if (!SystemInfo.supportsComputeShaders || !SystemInfo.supportsAsyncGPUReadback) return null;
            var cs = Resources.Load<ComputeShader>("InfluenceField");
            return cs != null ? new InfluenceComputeBackend(cs) : null;
        }

        public bool Compute(InfluenceUnit[] units, int count, int gridN, float cellSize, float[] field)
        {
            int cells = gridN * gridN * InfluenceMap.CHANNELS;
            if (fieldBuffer == null || fieldBuffer.count != cells)
            {
                fieldBuffer?.Release();
                fieldBuffer = new ComputeBuffer(cells, sizeof(float));
                latest = new float[cells];
                hasLatest = false;
            }
            if (unitBuffer == null || unitBuffer.count < Mathf.Max(1, count))
            {
                unitBuffer?.Release();
                unitBuffer = new ComputeBuffer(Mathf.NextPowerOfTwo(Mathf.Max(64, count)), 32);
            }

            if (!pending)
            {
                unitBuffer.SetData(units, 0, 0, count);
                shader.SetInt("_Count", count);
                shader.SetInt("_N", gridN);
                shader.SetFloat("_Cell", cellSize);
                shader.SetBuffer(kernel, "_Units", unitBuffer);
                shader.SetBuffer(kernel, "_Field", fieldBuffer);
                int groups = (gridN + 7) / 8;
                sw.Restart();
                shader.Dispatch(kernel, groups, groups, 1);
                pending = true;
                AsyncGPUReadback.Request(fieldBuffer, OnReadback);
            }

            if (!hasLatest) return false;
            System.Array.Copy(latest, field, cells);
            return true;
        }

        void OnReadback(AsyncGPUReadbackRequest req)
        {
            pending = false;
            if (req.hasError || latest == null) return;
            NativeArray<float> data = req.GetData<float>();
            if (data.Length != latest.Length) return;
            data.CopyTo(latest);
            hasLatest = true;
            LastMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        public void Dispose()
        {
            unitBuffer?.Release();
            fieldBuffer?.Release();
            unitBuffer = fieldBuffer = null;
        }
    }
}
