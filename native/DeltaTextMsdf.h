#pragma once

#include <stdint.h>

#ifdef _WIN32
#define DELTATEXT_MSDF_API __declspec(dllexport)
#else
#define DELTATEXT_MSDF_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct deltatext_msdf_point_t { float x, y; uint8_t kind; } deltatext_msdf_point_t;
typedef struct deltatext_msdf_contour_t { const deltatext_msdf_point_t* points; int32_t count; } deltatext_msdf_contour_t;
/* Returned pixels are owned by the native bridge until deltatext_msdf_free.
   They are RGB8, row-major, Y-up, with stride in bytes and a symmetric
   distance range in font units. The managed side copies then frees them. */
typedef struct deltatext_msdf_bitmap_t { uint8_t* pixels; int32_t length, width, height, stride; float distance_range; } deltatext_msdf_bitmap_t;

enum { DELTATEXT_MSDF_OK = 0, DELTATEXT_MSDF_INVALID_ARGUMENT = 1, DELTATEXT_MSDF_INVALID_CONTOUR = 2, DELTATEXT_MSDF_OUT_OF_MEMORY = 3, DELTATEXT_MSDF_GENERATION_FAILED = 4 };
enum { DELTATEXT_POINT_LINE = 0, DELTATEXT_POINT_QUADRATIC_CONTROL = 1, DELTATEXT_POINT_CUBIC_CONTROL = 2, DELTATEXT_POINT_CUBIC_END = 3 };

typedef struct deltatext_font_metrics_t {
    int32_t units_per_em;
    int32_t ascender;
    int32_t descender;
    int32_t line_gap;
} deltatext_font_metrics_t;

typedef struct deltatext_glyph_metrics_t {
    uint32_t glyph_id;
    int32_t advance_x;
    int32_t advance_y;
    int32_t bearing_x;
    int32_t bearing_y;
    int32_t width;
    int32_t height;
    int32_t units_per_em;
} deltatext_glyph_metrics_t;

DELTATEXT_MSDF_API int deltatext_generate_msdf_from_contours(
    const deltatext_msdf_contour_t* contours, int32_t contour_count,
    int32_t pixel_size,
    int32_t units_per_em,
    int32_t padding,
    float distance_range,
    uint32_t edge_seed,
    deltatext_msdf_bitmap_t* out_bitmap);
DELTATEXT_MSDF_API void deltatext_msdf_free(void* pixels);

#ifdef __cplusplus
}
#endif
