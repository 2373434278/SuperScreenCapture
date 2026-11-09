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

typedef struct {
    uint32_t display_id;
    int32_t x;
    int32_t y;
    uint32_t width;
    uint32_t height;
    double display_scale;
    uint32_t fps;
    uint32_t bitrate_mbps;
    uint32_t output_width;
    uint32_t output_height;
    int32_t include_microphone;
} ssc_recording_config;

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

/* Recording states: 0 idle, 1 recording, 2 paused, 3 finalizing, 4 completed, 5 failed. */
int32_t ssc_recording_start(const char *path, const ssc_recording_config *config);
int32_t ssc_recording_pause(void);
int32_t ssc_recording_resume(void);
int32_t ssc_recording_stop(void);
int32_t ssc_recording_set_microphone(int32_t enabled);
int32_t ssc_recording_state(void);
double ssc_recording_elapsed_seconds(void);
double ssc_recording_mic_level(void);
int32_t ssc_microphone_permission_status(void);
int32_t ssc_request_microphone_permission(void);

int32_t ssc_ocr_image(
    const uint8_t *pixels,
    uint32_t width,
    uint32_t height,
    uint32_t stride,
    const char *languages,
    char **out_json);
void ssc_free_string(char *value);

/* Global hotkeys. Callbacks are delivered on the main thread.
   Returns 0 on success; -9878 means the shortcut is already in use. */
typedef void (*ssc_hotkey_callback)(uint32_t id, void *user_data);

int32_t ssc_hotkey_register(uint32_t id, uint32_t keycode, uint32_t carbon_modifiers,
                            ssc_hotkey_callback callback, void *user_data);
int32_t ssc_hotkey_unregister(uint32_t id);
void ssc_hotkey_unregister_all(void);

/* Auto start states: 0 disabled, 1 enabled, 2 requires approval, 3 unavailable, 4 failed. */
int32_t ssc_autostart_status(void);
int32_t ssc_autostart_set(int32_t enabled);

#ifdef __cplusplus
}
#endif

#endif
