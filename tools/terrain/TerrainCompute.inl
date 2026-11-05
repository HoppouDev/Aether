/* Optional ABI1 capability64. All calls share the owning FNA SDL command stream. */
FNA3DAPI SDL_GPUComputePipeline *TA_TerrainCreateExpandPipeline(FNA3D_Device *device,const void *code,size_t size)
{
    SDLGPU_Renderer *r=TA_Renderer(device);
    if(!r || !code || size<20 || !(TA_TerrainCapabilities(device)&64u))return NULL;
    SDL_GPUComputePipelineCreateInfo info={0};
    info.code=code;info.code_size=size;info.entrypoint="main";info.format=SDL_GPU_SHADERFORMAT_SPIRV;
    info.num_readonly_storage_buffers=1;info.num_readwrite_storage_buffers=1;info.num_uniform_buffers=1;
    info.threadcount_x=64;info.threadcount_y=info.threadcount_z=1;
    return SDL_CreateGPUComputePipeline(r->device,&info);
}
FNA3DAPI void TA_TerrainReleaseExpandPipeline(FNA3D_Device *device,SDL_GPUComputePipeline *pipeline)
{
    SDLGPU_Renderer *r=TA_Renderer(device);
    if(r && pipeline)SDL_ReleaseGPUComputePipeline(r->device,pipeline);
}
FNA3DAPI int32_t TA_TerrainExpand(FNA3D_Device *device,SDL_GPUComputePipeline *pipeline,SDL_GPUBuffer *input,SDL_GPUBuffer *output,uint32_t first,uint32_t count,uint32_t destination)
{
    SDLGPU_Renderer *r=TA_Renderer(device);
    if(!r || !pipeline || !input || !output || input==output || !count || count>4096)return 0;
    /* Upload helpers close copy passes before returning. End graphics here and
     * isolate every dispatch in its own pass, including dependent expansions. */
    SDLGPU_INTERNAL_EndRenderPass(r);
    SDL_GPUStorageBufferReadWriteBinding binding={0};binding.buffer=output;
    SDL_GPUComputePass *pass=SDL_BeginGPUComputePass(r->renderCommandBuffer,NULL,0,&binding,1);
    SDL_BindGPUComputePipeline(pass,pipeline);
    SDL_BindGPUComputeStorageBuffers(pass,0,&input,1);
    uint32_t uniforms[4]={first,count,destination,0};
    SDL_PushGPUComputeUniformData(r->renderCommandBuffer,0,uniforms,sizeof(uniforms));
    SDL_DispatchGPUCompute(pass,(count+63)/64,1,1);
    SDL_EndGPUComputePass(pass);
    return 1;
}
