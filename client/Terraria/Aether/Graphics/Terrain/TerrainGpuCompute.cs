using Terraria;
using System.Threading;
using Microsoft.Xna.Framework;
using System;
using System.Runtime.InteropServices;

namespace Aether.Graphics.Terrain;

public sealed class TerrainGpuComputePipeline : TerrainGpuResource
{
    internal TerrainGpuComputePipeline(TerrainGpuContext owner,nint handle,long bytes):base(owner,handle,bytes){}
    internal override void ReleaseNative()=>Owner.ReleaseComputePipelineHandle(Handle);
}

public sealed partial class TerrainGpuContext
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate nint ExpandCreate(nint device,nint code,nuint size);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void ExpandRelease(nint device,nint pipeline);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ExpandDispatch(nint device,nint pipeline,nint input,nint output,uint first,uint count,uint destination);
    private ExpandCreate? createExpand;
    private ExpandRelease? releaseExpand;
    private ExpandDispatch? expand;
    private void InitializeComputeGeometryApi()
    {
        if((Capabilities&TerrainGpuCapabilities.GeometryCompute)==0)return;
        createExpand=Load<ExpandCreate>(library,"TA_TerrainCreateExpandPipeline");
        releaseExpand=Load<ExpandRelease>(library,"TA_TerrainReleaseExpandPipeline");
        expand=Load<ExpandDispatch>(library,"TA_TerrainExpand");
    }
    public TerrainGpuComputePipeline CreateExpandPipeline(byte[] spirv)
    {
        CheckOwner();ValidateShader(spirv);
        if(createExpand is null)throw new NotSupportedException("Terrain geometry compute unavailable");
        long bytes=65536L+spirv.Length;ReserveCheck(bytes);
        var pin=GCHandle.Alloc(spirv,GCHandleType.Pinned);
        try
        {
            nint handle=createExpand(device,pin.AddrOfPinnedObject(),(nuint)spirv.Length);
            if(handle==0)throw new InvalidOperationException(LastError);
            var result=new TerrainGpuComputePipeline(this,handle,bytes);buffers.Add(result);resident+=bytes;return result;
        }
        finally{pin.Free();}
    }
    internal void ReleaseComputePipelineHandle(nint handle)=>releaseExpand!(device,handle);
    public void Expand(TerrainGpuComputePipeline pipeline,TerrainGpuBuffer input,TerrainGpuBuffer output,int first,int count,int destination)
    {
        CheckOwner();CheckLive(pipeline);CheckLive(input);CheckLive(output);
        if(expand is null)throw new NotSupportedException("Terrain geometry compute unavailable");
        if(ReferenceEquals(input,output) || (input.Usage&TerrainBufferUsage.ComputeRead)==0 || (output.Usage&TerrainBufferUsage.ComputeWrite)==0)
            throw new ArgumentException("Compute requires distinct readonly input and writable output buffers");
        if(first<0||count<1||count>4096||destination<0||(long)first+count>input.Size/32||(long)destination+count*6L>output.Size/32)
            throw new ArgumentOutOfRangeException(nameof(count));
        using var marker=Marker("Terrain compute expansion");
        if(expand(device,pipeline.Handle,input.Handle,output.Handle,(uint)first,(uint)count,(uint)destination)==0)
            throw new InvalidOperationException(LastError);
    }
    // Explicit diagnostics only: this submits and waits, never use in frame work.
}
