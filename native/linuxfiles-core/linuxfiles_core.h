/* Copyright (c) Files Community. Licensed under the MIT License. */
#ifndef LINUXFILES_CORE_H
#define LINUXFILES_CORE_H
#include <stddef.h>
#include <stdint.h>

typedef struct lfc_scanner lfc_scanner;
typedef struct {
    uint64_t size, inode;
    int64_t modified_seconds;
    uint64_t mount_id;
    uint32_t mode, owner_id, modified_nanoseconds, dev_major, dev_minor, mask;
    uint32_t name_offset, name_length;
} lfc_record;

/* Return 0 or positive errno (panic -> EIO). No concurrent calls on a handle.
   Paths are NUL-terminated. Buffers/counters must be valid, writable and disjoint.
   Records are 64 bytes, aligned to 8. Names are raw bytes, without terminators.
   lfc_next: count=0 on EOF; counters cleared on errors, partial batches discarded.
   ERANGE means the arena cannot hold one name; retry with a larger arena.
   Other errors require closing the handle. Outputs remain caller-owned.
   lfc_close frees the handle/descriptor exactly once. Invalid non-null pointers
   and use-after-close are caller errors; they cannot be validated by this ABI. */
int32_t lfc_open(const char *path, lfc_scanner **handle);
int32_t lfc_next(lfc_scanner *handle, lfc_record *records, size_t capacity,
                 uint8_t *names, size_t name_capacity, size_t *count, size_t *used);
int32_t lfc_close(lfc_scanner *handle);
size_t lfc_record_size(void);
#endif
