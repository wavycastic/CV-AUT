#include <stddef.h>

#ifdef _WIN32
#define SIMPLIMIXI_EXPORT __declspec(dllexport)
#else
#define SIMPLIMIXI_EXPORT __attribute__((visibility("default")))
#endif

static const unsigned char MAGIC[] = { 0x53, 0x4D, 0x54, 0x50, 0x01 };
static const unsigned char SEED[] = {
    0x31, 0xA4, 0x5C, 0x27, 0xE8, 0x09, 0xD3, 0x76,
    0x42, 0xBD, 0x18, 0xC1, 0x6F, 0x90, 0x2A, 0x55,
    0xCE, 0x03, 0xB7, 0x64, 0x1D, 0x88, 0xF2, 0x0B,
    0x79, 0xE1, 0x34, 0xAC, 0x5A, 0x17, 0xC9, 0x60
};
static const unsigned char MASK[] = {
    0x4F, 0x12, 0xE0, 0x99, 0x3B, 0xC6, 0x70, 0x2D,
    0x84, 0x5E, 0xA9, 0x01, 0xF3, 0x6C, 0x1A, 0xD5
};

static void create_key(unsigned char key[24])
{
    for (int i = 0; i < 24; i++)
    {
        int mixed = SEED[i] ^ MASK[(i * 7 + 3) % 16] ^ (i * 29 + 0x41);
        key[i] = (unsigned char)(((mixed << 3) | ((unsigned int)mixed >> 5)) & 0xFF);
    }
}

SIMPLIMIXI_EXPORT int simplimixi_decode_template(
    const unsigned char* input,
    int input_len,
    unsigned char* output,
    int output_capacity,
    int* output_len)
{
    if (output_len == NULL)
    {
        return 1;
    }

    *output_len = 0;
    if (input == NULL || output == NULL || input_len <= (int)sizeof(MAGIC))
    {
        return 2;
    }

    for (int i = 0; i < (int)sizeof(MAGIC); i++)
    {
        if (input[i] != MAGIC[i])
        {
            return 3;
        }
    }

    int decoded_len = input_len - (int)sizeof(MAGIC);
    if (output_capacity < decoded_len)
    {
        return 4;
    }

    unsigned char key[24];
    create_key(key);
    for (int i = 0; i < decoded_len; i++)
    {
        output[i] = (unsigned char)(input[i + sizeof(MAGIC)] ^ key[i % 24]);
    }

    *output_len = decoded_len;
    return 0;
}
