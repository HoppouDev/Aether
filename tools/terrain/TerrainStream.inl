/* Streamed-upload and atlas-copy extension; private borrowed FNA texture handles
 * are used only during the owner-thread call, never retained by managed workers. */
FNA3DAPI int32_t TA_TerrainCanDrawCurrentTarget(FNA3D_Device *device)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    return r && r->nextRenderPassColorAttachmentCount == 1 && !r->nextRenderPassDepthStencilAttachment &&
        r->nextRenderPassMultisampleCount == SDL_GPU_SAMPLECOUNT_1 && r->nextRenderPassColorAttachments[0] &&
        r->nextRenderPassColorAttachments[0]->createInfo.format == SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM;
}
FNA3DAPI int32_t TA_TerrainUploadBufferRegion(FNA3D_Device *device, SDL_GPUBuffer *buffer, uint32_t offset, const void *bytes, uint32_t size)
{
    return TA_UploadBuffer(device, buffer, offset, bytes, size);
}
FNA3DAPI int32_t TA_TerrainCopyFnaImage(FNA3D_Device *device, FNA3D_Texture *source, SDL_GPUTexture *target, uint32_t x, uint32_t y, uint32_t width, uint32_t height, uint32_t padding, uint32_t clear)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    SDLGPU_TextureHandle *image = (SDLGPU_TextureHandle *)source;
    if (!r || !image || !target || image->texture == target || !width || !height || padding > 8 || x < padding || y < padding ||
        image->createInfo.type != SDL_GPU_TEXTURETYPE_2D || image->createInfo.width != width || image->createInfo.height != height ||
        image->createInfo.sample_count != SDL_GPU_SAMPLECOUNT_1) return 0;
    SDLGPU_INTERNAL_EndRenderPass(r);
    uint32_t sourceX[3] = {0,0,width-1}, sourceY[3] = {0,0,height-1};
    uint32_t sourceW[3] = {1,width,1}, sourceH[3] = {1,height,1};
    uint32_t targetX[3] = {x-padding,x,x+width}, targetY[3] = {y-padding,y,y+height};
    uint32_t targetW[3] = {padding,width,padding}, targetH[3] = {padding,height,padding};
    for (uint32_t row=0;row<3;row++) for(uint32_t col=0;col<3;col++)
    {
        if (!targetW[col] || !targetH[row]) continue;
        SDL_GPUBlitInfo blit = {0};
        blit.source.texture = image->texture; blit.source.x = sourceX[col]; blit.source.y = sourceY[row];
        blit.source.w = sourceW[col]; blit.source.h = sourceH[row];
        blit.destination.texture = target; blit.destination.x = targetX[col]; blit.destination.y = targetY[row];
        blit.destination.w = targetW[col]; blit.destination.h = targetH[row];
        blit.filter = SDL_GPU_FILTER_NEAREST; blit.load_op = clear ? SDL_GPU_LOADOP_CLEAR : SDL_GPU_LOADOP_LOAD;
        SDL_BlitGPUTexture(r->renderCommandBuffer, &blit); clear = 0;
    }
    return 1;
}
