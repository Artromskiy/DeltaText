#include "DeltaTextMsdf.h"
#include "../third_party/msdfgen/msdfgen.h"
#include "../third_party/msdfgen/core/edge-coloring.h"
#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <new>

using namespace msdfgen;

static double coord(float value) { return static_cast<double>(value); }

static bool addContour(Shape &shape, const deltatext_msdf_contour_t &input) {
    if (!input.points || input.count < 2) return false;
    Contour &contour = shape.addContour();
    const auto &first = input.points[0];
    Point2 start(coord(first.x), coord(first.y));
    Point2 current = start;
    for (int32_t i = 1; i < input.count;) {
        const auto &point = input.points[i];
        if (point.kind == DELTATEXT_POINT_QUADRATIC_CONTROL && i + 1 < input.count) {
            const auto &end = input.points[i + 1];
            contour.addEdge(EdgeSegment::create(current, Point2(coord(point.x), coord(point.y)), Point2(coord(end.x), coord(end.y))));
            current = Point2(coord(end.x), coord(end.y));
            i += 2;
        } else if (point.kind == DELTATEXT_POINT_CUBIC_CONTROL && i + 2 < input.count) {
            const auto &control2 = input.points[i + 1];
            const auto &end = input.points[i + 2];
            contour.addEdge(EdgeSegment::create(current, Point2(coord(point.x), coord(point.y)), Point2(coord(control2.x), coord(control2.y)), Point2(coord(end.x), coord(end.y))));
            current = Point2(coord(end.x), coord(end.y));
            i += 3;
        } else {
            contour.addEdge(EdgeSegment::create(current, Point2(coord(point.x), coord(point.y))));
            current = Point2(coord(point.x), coord(point.y));
            ++i;
        }
    }
    if (current != start)
        contour.addEdge(EdgeSegment::create(current, start));
    return true;
}

extern "C" DELTATEXT_MSDF_API int deltatext_generate_msdf_from_contours(
    const deltatext_msdf_contour_t *contours, int32_t contour_count,
    int32_t pixel_size, int32_t units_per_em, int32_t padding, float distance_range,
    uint32_t edge_seed, deltatext_msdf_bitmap_t *out_bitmap) {
    if (!contours || contour_count <= 0 || pixel_size <= 0 || units_per_em <= 0 || padding < 0 || !(distance_range > 0) || !out_bitmap)
        return DELTATEXT_MSDF_INVALID_ARGUMENT;
    *out_bitmap = {};
    try {
        Shape shape;
        for (int32_t i = 0; i < contour_count; ++i)
            if (!addContour(shape, contours[i])) return DELTATEXT_MSDF_INVALID_ARGUMENT;
        if (!shape.validate()) return DELTATEXT_MSDF_INVALID_CONTOUR;
        shape.normalize();
        shape.orientContours();
        const Shape::Bounds bounds = shape.getBounds();
        const double scale = static_cast<double>(pixel_size) / units_per_em;
        const int width = std::max(1, static_cast<int>(std::ceil((bounds.r - bounds.l) * scale)) + padding * 2 + 2);
        const int height = std::max(1, static_cast<int>(std::ceil((bounds.t - bounds.b) * scale)) + padding * 2 + 2);
        const Vector2 pixelScale(scale, scale);
        const Vector2 translate(padding + 1 - bounds.l * scale, padding + 1 - bounds.b * scale);
        const Projection projection(pixelScale, translate);
        Bitmap<float, 3> bitmap(width, height, Y_UPWARD);
        edgeColoringSimple(shape, 3.0, edge_seed);
        MSDFGeneratorConfig config;
        config.errorCorrection.mode = ErrorCorrectionConfig::DISABLED;
        generateMSDF(bitmap, shape, projection, Range(distance_range), config);
        const int length = width * height * 3;
        auto *pixels = static_cast<uint8_t *>(std::malloc(static_cast<size_t>(length)));
        if (!pixels) return DELTATEXT_MSDF_OUT_OF_MEMORY;
        for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
            const float *source = bitmap(x, y);
            for (int c = 0; c < 3; ++c) {
                const double raw = 0.5 + source[c] / (2.0 * distance_range);
                const double normalized = std::max(0.0, std::min(1.0, raw));
                pixels[(y * width + x) * 3 + c] = static_cast<uint8_t>(std::lround(normalized * 255.0));
            }
        }
        out_bitmap->pixels = pixels;
        out_bitmap->length = length;
        out_bitmap->width = width;
        out_bitmap->height = height;
        out_bitmap->stride = width * 3;
        out_bitmap->distance_range = distance_range;
        return DELTATEXT_MSDF_OK;
    } catch (const std::bad_alloc &) { return DELTATEXT_MSDF_OUT_OF_MEMORY; }
      catch (...) { return DELTATEXT_MSDF_GENERATION_FAILED; }
}

extern "C" DELTATEXT_MSDF_API void deltatext_msdf_free(void *pixels) { std::free(pixels); }
