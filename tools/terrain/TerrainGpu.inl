/* Aether extension to the pinned FNA3D SDL GPU backend. This is not upstream FNA3D.
 * Included in FNA3D_Driver_SDL.c so all command-buffer ownership stays in that driver.
 * ABI 1; all entry points must be called from the graphics device's owning thread.
 */
#include <stdlib.h>

#define TA_TERRAIN_ABI 1u
#define TA_CAP_VULKAN 1u
#define TA_CAP_COMPUTE 2u
#define TA_CAP_BUFFERS 4u
#define TA_CAP_MARKERS 8u
#define TA_CAP_DRAW 16u
#define TA_CAP_STREAMED 32u
#define TA_CAP_GEOMETRY_COMPUTE 64u

static SDLGPU_Renderer *TA_Renderer(FNA3D_Device *device)
{
    if (device == NULL || device->GetSysRenderer != SDLGPU_GetSysRenderer)
    {
        SDL_SetError("Aether requires the FNA3D SDL GPU backend");
        return NULL;
    }
    return (SDLGPU_Renderer *)device->driverData;
}
FNA3DAPI uint32_t TA_TerrainAbi(void) { return TA_TERRAIN_ABI; }
FNA3DAPI const char *TA_TerrainError(void) { return SDL_GetError(); }
FNA3DAPI uint32_t TA_TerrainCapabilities(FNA3D_Device *device)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (r == NULL || SDL_strcmp(SDL_GetGPUDeviceDriver(r->device), "vulkan") != 0) return 0;
    uint32_t result = TA_CAP_VULKAN | TA_CAP_BUFFERS | TA_CAP_MARKERS | TA_CAP_DRAW | TA_CAP_STREAMED;
    if ((SDL_GetGPUShaderFormats(r->device) & SDL_GPU_SHADERFORMAT_SPIRV) &&
        SDL_GPUTextureSupportsFormat(r->device, SDL_GPU_TEXTUREFORMAT_R8G8B8A8_UNORM,
            SDL_GPU_TEXTURETYPE_2D, SDL_GPU_TEXTUREUSAGE_COMPUTE_STORAGE_WRITE | SDL_GPU_TEXTUREUSAGE_SAMPLER))
        result |= TA_CAP_COMPUTE | TA_CAP_GEOMETRY_COMPUTE;
    return result;
}
FNA3DAPI void TA_TerrainPushMarker(FNA3D_Device *device, const char *name)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r || !name) return;
    if (r->terrainMarkerCount == 16) { r->terrainMarkerOverflow++; return; }
    SDL_strlcpy(r->terrainMarkers[r->terrainMarkerCount], name, 96);
    SDL_PushGPUDebugGroup(r->renderCommandBuffer, r->terrainMarkers[r->terrainMarkerCount]);
    r->terrainMarkerCount++;
}
FNA3DAPI void TA_TerrainPopMarker(FNA3D_Device *device)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r) return;
    if (r->terrainMarkerOverflow) { r->terrainMarkerOverflow--; return; }
    if (!r->terrainMarkerCount) return;
    SDL_PopGPUDebugGroup(r->renderCommandBuffer);
    r->terrainMarkerCount--;
}
FNA3DAPI SDL_GPUBuffer *TA_TerrainCreateBuffer(FNA3D_Device *device, uint32_t size, uint32_t usage)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (r == NULL || size == 0 || size > 536870912u || usage == 0) return NULL;
    SDL_GPUBufferCreateInfo info = {0};
    info.size = size;
    info.usage = usage;
    return SDL_CreateGPUBuffer(r->device, &info);
}
static int32_t TA_UploadBuffer(FNA3D_Device *device, SDL_GPUBuffer *buffer, uint32_t offset, const void *bytes, uint32_t size)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (r == NULL || buffer == NULL || bytes == NULL || size == 0) return 0;
    SDL_GPUTransferBufferCreateInfo info = {0};
    info.usage = SDL_GPU_TRANSFERBUFFERUSAGE_UPLOAD;
    info.size = size;
    SDL_GPUTransferBuffer *transfer = SDL_CreateGPUTransferBuffer(r->device, &info);
    if (transfer == NULL) return 0;
    void *mapped = SDL_MapGPUTransferBuffer(r->device, transfer, false);
    if (mapped == NULL) { SDL_ReleaseGPUTransferBuffer(r->device, transfer); return 0; }
    SDL_memcpy(mapped, bytes, size);
    SDL_UnmapGPUTransferBuffer(r->device, transfer);
    SDLGPU_INTERNAL_EndRenderPass(r);
    SDL_GPUCopyPass *copy = SDL_BeginGPUCopyPass(r->renderCommandBuffer);
    SDL_GPUTransferBufferLocation source = {0}; source.transfer_buffer = transfer;
    SDL_GPUBufferRegion target = {0}; target.buffer = buffer; target.offset = offset; target.size = size;
    SDL_UploadToGPUBuffer(copy, &source, &target, false);
    SDL_EndGPUCopyPass(copy);
    SDL_ReleaseGPUTransferBuffer(r->device, transfer);
    return 1;
}
FNA3DAPI int32_t TA_TerrainUploadBuffer(FNA3D_Device *device, SDL_GPUBuffer *buffer, const void *bytes, uint32_t size)
{
    return TA_UploadBuffer(device, buffer, 0, bytes, size);
}
FNA3DAPI void TA_TerrainReleaseBuffer(FNA3D_Device *device, SDL_GPUBuffer *buffer)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (r != NULL && buffer != NULL) SDL_ReleaseGPUBuffer(r->device, buffer);
}
typedef struct TA_TerrainFence { SDL_GPUFence *upload; SDL_GPUFence *render; } TA_TerrainFence;
FNA3DAPI TA_TerrainFence *TA_TerrainSubmitFence(FNA3D_Device *device)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r) return NULL;
    TA_TerrainFence *fence = SDL_calloc(1, sizeof(TA_TerrainFence));
    if (!fence) return NULL;
    SDLGPU_INTERNAL_FlushUploadCommandsAndAcquireFence(r, &fence->upload);
    SDLGPU_INTERNAL_EndRenderPass(r);
    for (uint32_t i = 0; i < r->terrainMarkerCount; i += 1)
        SDL_PopGPUDebugGroup(r->renderCommandBuffer);
    fence->render = SDL_SubmitGPUCommandBufferAndAcquireFence(r->renderCommandBuffer);
    SDLGPU_INTERNAL_ResetRenderCommandBufferState(r);
    if (!fence->upload || !fence->render)
    {
        if (fence->upload) SDL_ReleaseGPUFence(r->device, fence->upload);
        if (fence->render) SDL_ReleaseGPUFence(r->device, fence->render);
        SDL_free(fence); return NULL;
    }
    return fence;
}
FNA3DAPI int32_t TA_TerrainQueryFence(FNA3D_Device *device, TA_TerrainFence *fence)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    return r && fence && SDL_QueryGPUFence(r->device, fence->upload) && SDL_QueryGPUFence(r->device, fence->render);
}
FNA3DAPI void TA_TerrainReleaseFence(FNA3D_Device *device, TA_TerrainFence *fence)
{
    SDLGPU_Renderer *r = TA_Renderer(device);
    if (!r || !fence) return;
    SDL_ReleaseGPUFence(r->device, fence->upload);
    SDL_ReleaseGPUFence(r->device, fence->render);
    SDL_free(fence);
}

#include "TerrainDraw.inl"

#include "TerrainStream.inl"

#include "TerrainCompute.inl"
