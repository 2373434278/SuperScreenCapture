#ifndef SSCMAC_H
#define SSCMAC_H

#include <stdint.h>
#include <stddef.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef struct {
    uint32_t width;
    uint32_t height;
    uint32_t stride;
    uint8_t *pixels;
} ssc_image;

typedef struct {
    uint32_t id;
    int32_t x;
    int32_t y;
    uint32_t width;
    uint32_t height;
    double scale;
    int32_t primary;
} ssc_display;

typedef struct {
    uint32_t id;
    int32_t x;
    int32_t y;
    uint32_t width;
    uint32_t height;
} ssc_window;

int32_t ssc_display_count(void);
int32_t ssc_display_get(int32_t index, ssc_display *out);
int32_t ssc_window_at_point(int32_t x, int32_t y, ssc_window *out);
int32_t ssc_capture_display(uint32_t display_id, ssc_image *out);
int32_t ssc_capture_window(uint32_t window_id, ssc_image *out);
int32_t ssc_preflight_permission(void);
int32_t ssc_request_permission(void);
int32_t ssc_copy_image_to_clipboard(const uint8_t *pixels, uint32_t width, uint32_t height, uint32_t stride);
const char *ssc_last_error(void);
void ssc_free_image(ssc_image *image);

#ifdef __cplusplus
}
#endif

#endif
