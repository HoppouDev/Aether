/* Optional ABI1 drawing extension. Only the owning graphics thread may call it. */

typedef struct TA_DrawPipeline { SDL_GPUGraphicsPipeline *pipeline; SDL_GPUSampler *sampler; } TA_DrawPipeline;
static SDL_GPUTextureFormat TA_ImageFormat(uint32_t format)
{
    return format == 1 ? SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM :
        format == 2 ? SDL_GPU_TEXTUREFORMAT_R32G32B32A32_FLOAT : SDL_GPU_TEXTUREFORMAT_INVALID;
}
FNA3DAPI SDL_GPUTexture *TA_TerrainCreateImage(FNA3D_Device *device, uint32_t width, uint32_t height, uint32_t format, uint32_t renderTarget)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r || !width || !height || width > 8192 || height > 8192 || !TA_ImageFormat(format)) return NULL;
    SDL_GPUTextureCreateInfo info = {0};
    info.type = SDL_GPU_TEXTURETYPE_2D; info.format = TA_ImageFormat(format);
    info.width = width; info.height = height; info.layer_count_or_depth = 1; info.num_levels = 1;
    info.usage = SDL_GPU_TEXTUREUSAGE_SAMPLER | (renderTarget ? SDL_GPU_TEXTUREUSAGE_COLOR_TARGET : 0);
    info.sample_count = SDL_GPU_SAMPLECOUNT_1;
    return SDL_CreateGPUTexture(r->device, &info);
}
FNA3DAPI void TA_TerrainReleaseImage(FNA3D_Device *device, SDL_GPUTexture *image)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (r && image) SDL_ReleaseGPUTexture(r->device, image);
}
FNA3DAPI int32_t TA_TerrainUploadImage(FNA3D_Device *device, SDL_GPUTexture *image, uint32_t x, uint32_t y, uint32_t width, uint32_t height, const void *bytes, uint32_t size)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r || !image || !bytes || !size || !width || !height) return 0;
    SDL_GPUTransferBufferCreateInfo info = {0}; info.usage = SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD; info.size = size;
    SDL_GPUTransferBuffer *transfer = SDL_CreateGPUTransferBuffer(r->device, &info);
    if (!transfer) return 0;
    void *mapped = SDL_MapGPUTransferBuffer(r->device, transfer, false);
    if (!mapped) { SDL_ReleaseGPUTransferBuffer(r->device, transfer); return 0; }
    SDL_memcpy(mapped, bytes, size); SDL_UnmapGPUTransferBuffer(r->device, transfer);
    SDLGPU_INTERNAL_EndRenderPass(r);
    SDL_GPUCopyPass *copy = SDL_BeginGPUCopyPass(r->renderCommandBuffer);
    SDL_GPUTextureTransferInfo source = {0}; source.transfer_buffer = transfer; source.pixels_per_row = width; source.rows_per_layer = height;
    SDL_GPUTextureRegion target = {0}; target.texture = image; target.x = x; target.y = y; target.w = width; target.h = height; target.d = 1;
    SDL_UploadToGPUTexture(copy, &source, &target, false);
    SDL_EndGPUCopyPass(copy); SDL_ReleaseGPUTransferBuffer(r->device, transfer);
    return 1;
}
FNA3DAPI TA_DrawPipeline *TA_TerrainCreateDrawPipeline(FNA3D_Device *device, const void *vertexCode, size_t vertexSize, const void *fragmentCode, size_t fragmentSize)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r || !vertexCode || !fragmentCode || vertexSize < 20 || fragmentSize < 20) return NULL;
    SDL_GPUShaderCreateInfo info = {0};
    info.code = vertexCode; info.code_size = vertexSize; info.entrypoint = "main"; info.format = SDL_GPU_SHADERFORMAT_SPIRV;
    info.stage = SDL_GPU_SHADERSTAGE_VERTEX; info.num_uniform_buffers = 1;
    SDL_GPUShader *vertex = SDL_CreateGPUShader(r->device, &info);
    if (!vertex) return NULL;
    info.code = fragmentCode; info.code_size = fragmentSize; info.stage = SDL_GPU_SHADERSTAGE_FRAGMENT; info.num_samplers = 2;
    SDL_GPUShader *fragment = SDL_CreateGPUShader(r->device, &info);
    if (!fragment) { SDL_ReleaseGPUShader(r->device, vertex); return NULL; }
    SDL_GPUVertexBufferDescription buffer = {0}; buffer.pitch = 32; buffer.input_rate = SDL_GPU_VERTEXINPUTRATE_VERTEX;
    SDL_GPUVertexAttribute attributes[4] = {{0}};
    attributes[0].location = 0; attributes[0].format = SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2;
    attributes[1].location = 1; attributes[1].format = SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2; attributes[1].offset = 8;
    attributes[2].location = 2; attributes[2].format = SDL_GPU_VERTEXELEMENTFORMAT_FLOAT2; attributes[2].offset = 16;
    attributes[3].location = 3; attributes[3].format = SDL_GPU_VERTEXELEMENTFORMAT_UBYTE4_NORM; attributes[3].offset = 24;
    SDL_GPUColorTargetDescription color = {0}; color.format = SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
    color.blend_state.enable_blend = true;
    color.blend_state.src_color_blendfactor = SDL_GPU_BLENDFACTOR_ONE;
    color.blend_state.dst_color_blendfactor = SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA;
    color.blend_state.color_blend_op = SDL_GPU_BLENDOP_ADD;
    color.blend_state.src_alpha_blendfactor = SDL_GPU_BLENDFACTOR_ONE;
    color.blend_state.dst_alpha_blendfactor = SDL_GPU_BLENDFACTOR_ONE_MINUS_SRC_ALPHA;
    color.blend_state.alpha_blend_op = SDL_GPU_BLENDOP_ADD;
    SDL_GPUGraphicsPipelineCreateInfo pipeline = {0}; pipeline.vertex_shader = vertex; pipeline.fragment_shader = fragment;
    pipeline.vertex_input_state.vertex_buffer_descriptions = &buffer; pipeline.vertex_input_state.num_vertex_buffers = 1;
    pipeline.vertex_input_state.vertex_attributes = attributes; pipeline.vertex_input_state.num_vertex_attributes = 4;
    pipeline.primitive_type = SDL_GPU_PRIMITIVETYPE_TRIANGLELIST;
    pipeline.rasterizer_state.fill_mode = SDL_GPU_FILLMODE_FILL; pipeline.rasterizer_state.cull_mode = SDL_GPU_CULLMODE_NONE;
    pipeline.rasterizer_state.enable_depth_clip = true;
    pipeline.multisample_state.sample_count = SDL_GPU_SAMPLECOUNT_1;
    pipeline.target_info.color_target_descriptions = &color; pipeline.target_info.num_color_targets = 1;
    TA_DrawPipeline *result = SDL_calloc(1, sizeof(TA_DrawPipeline));
    if (result) result->pipeline = SDL_CreateGPUGraphicsPipeline(r->device, &pipeline);
    SDL_ReleaseGPUShader(r->device, vertex); SDL_ReleaseGPUShader(r->device, fragment);
    if (!result) return NULL;
    SDL_GPUSamplerCreateInfo sampler = {0}; sampler.min_filter = SDL_GPU_FILTER_NEAREST; sampler.mag_filter = SDL_GPU_FILTER_NEAREST;
    sampler.address_mode_u = sampler.address_mode_v = sampler.address_mode_w = SDL_GPU_SAMPLERADDRESSMODE_CLAMP_TO_EDGE;
    if (result->pipeline) result->sampler = SDL_CreateGPUSampler(r->device, &sampler);
    if (!result->pipeline || !result->sampler)
    { if (result->pipeline) SDL_ReleaseGPUGraphicsPipeline(r->device, result->pipeline); SDL_free(result); return NULL; }
    return result;
}
FNA3DAPI void TA_TerrainReleaseDrawPipeline(FNA3D_Device *device, TA_DrawPipeline *pipeline)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r || !pipeline) return;
    SDL_ReleaseGPUGraphicsPipeline(r->device, pipeline->pipeline); SDL_ReleaseGPUSampler(r->device, pipeline->sampler); SDL_free(pipeline);
}
FNA3DAPI int32_t TA_TerrainDraw(FNA3D_Device *device, TA_DrawPipeline *pipeline, SDL_GPUBuffer *vertices, uint32_t first, uint32_t count, SDL_GPUTexture *atlas, SDL_GPUTexture *light, const void *uniforms, SDL_GPUTexture *target, uint32_t clear)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r || !pipeline || !vertices || !count || !atlas || !light || !uniforms) return 0;
    if (!target && (r->nextRenderPassColorAttachmentCount != 1 || r->nextRenderPassDepthStencilAttachment ||
        r->nextRenderPassMultisampleCount != SDL_GPU_SAMPLECOUNT_1 ||
        r->nextRenderPassColorAttachments[0]->createInfo.format != SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM)) return 0;
    SDL_GPURenderPass *pass;
    if (target)
    {
        SDLGPU_INTERNAL_EndRenderPass(r);
        SDL_GPUColorTargetInfo color = {0}; color.texture = target; color.load_op = clear ? SDL_GPU_LOADOP_CLEAR : SDL_GPU_LOADOP_LOAD;
        color.store_op = SDL_GPU_STOREOP_STORE;
        pass = SDL_BeginGPURenderPass(r->renderCommandBuffer, &color, 1, NULL);
    }
    else { SDLGPU_INTERNAL_BeginRenderPass(r); pass = r->renderPass; }
    SDL_BindGPUGraphicsPipeline(pass, pipeline->pipeline);
    SDL_GPUBufferBinding binding = {0}; binding.buffer = vertices;
    SDL_BindGPUVertexBuffers(pass, 0, &binding, 1);
    SDL_GPUTextureSamplerBinding samplers[2] = {{0}};
    samplers[0].texture = atlas; samplers[1].texture = light; samplers[0].sampler = samplers[1].sampler = pipeline->sampler;
    SDL_BindGPUFragmentSamplers(pass, 0, samplers, 2);
    SDL_PushGPUVertexUniformData(r->renderCommandBuffer, 0, uniforms, 64);
    SDL_PushGPUFragmentUniformData(r->renderCommandBuffer, 0, (const uint8_t *)uniforms + 64, 64);
    SDL_DrawGPUPrimitives(pass, count, 1, first, 0);
    if (target) SDL_EndGPURenderPass(pass);
    /* FNA3D compares the cached pipeline pointer before binding; dirty flags alone
     * are insufficient. MojoShader re-uploads its uniforms on the next bind. */
    r->currentGraphicsPipeline = NULL; r->needNewGraphicsPipeline = 1;
    r->needVertexBufferBind = r->needVertexSamplerBind = r->needFragmentSamplerBind = 1;
    r->indexBufferBinding.buffer = NULL;
    return 1;
}
