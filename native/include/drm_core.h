#ifndef DRM_CORE_H
#define DRM_CORE_H

#include <stddef.h>
#include <stdint.h>

#ifdef _WIN32
#define DRM_API __declspec(dllexport)
#else
#define DRM_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef int32_t drm_result;
typedef uint64_t drm_session_handle;

typedef struct drm_open_request_v1 {
    uint32_t struct_size;
    uint32_t protocol_version;
    const uint8_t* signed_license;
    size_t signed_license_length;
    const uint8_t* content_id;
    size_t content_id_length;
} drm_open_request_v1;

DRM_API drm_result drm_open_session_v1(const drm_open_request_v1* request, drm_session_handle* session);
DRM_API drm_result drm_close_session_v1(drm_session_handle session);

#ifdef __cplusplus
}
#endif

#endif
