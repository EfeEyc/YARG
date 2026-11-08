#import <Foundation/Foundation.h>
#import <CoreGraphics/CoreGraphics.h>
#import <ImageIO/ImageIO.h>
#include <stdlib.h>
#include <string.h>

#ifdef __cplusplus
extern "C" {
#endif

unsigned char* load_image_from_memory(const unsigned char* data, int length, int* width, int* height, int* components)
{
    if (!data || length <= 0 || !width || !height || !components)
    {
        return NULL;
    }

    *width = 0;
    *height = 0;
    *components = 0;

    CFDataRef cfData = CFDataCreateWithBytesNoCopy(kCFAllocatorDefault, data, (CFIndex)length, kCFAllocatorNull);
    if (!cfData)
    {
        return NULL;
    }

    CGImageSourceRef source = CGImageSourceCreateWithData(cfData, NULL);
    if (!source)
    {
        CFRelease(cfData);
        return NULL;
    }

    CGImageRef cgImage = CGImageSourceCreateImageAtIndex(source, 0, NULL);
    CFRelease(source);
    CFRelease(cfData);

    if (!cgImage)
    {
        return NULL;
    }

    size_t imgWidth = CGImageGetWidth(cgImage);
    size_t imgHeight = CGImageGetHeight(cgImage);
    if (imgWidth == 0 || imgHeight == 0)
    {
        CGImageRelease(cgImage);
        return NULL;
    }

    size_t bytesPerPixel = 4;
    size_t bytesPerRow = imgWidth * bytesPerPixel;
    size_t bufferSize = bytesPerRow * imgHeight;

    unsigned char* rawData = (unsigned char*)malloc(bufferSize);
    if (!rawData)
    {
        CGImageRelease(cgImage);
        return NULL;
    }

    memset(rawData, 0, bufferSize);

    CGColorSpaceRef colorSpace = CGColorSpaceCreateDeviceRGB();
    CGBitmapInfo bitmapInfo = (CGBitmapInfo)kCGImageAlphaPremultipliedLast | kCGBitmapByteOrder32Big;
    CGContextRef context = CGBitmapContextCreate(
        rawData,
        imgWidth,
        imgHeight,
        8,
        bytesPerRow,
        colorSpace,
        bitmapInfo
    );
    CGColorSpaceRelease(colorSpace);

    if (!context)
    {
        free(rawData);
        CGImageRelease(cgImage);
        return NULL;
    }

    CGContextDrawImage(context, CGRectMake(0, 0, (CGFloat)imgWidth, (CGFloat)imgHeight), cgImage);
    CGContextRelease(context);
    CGImageRelease(cgImage);

    // Unpremultiply alpha to match stb_image's raw RGBA output
    size_t pixelCount = imgWidth * imgHeight;
    for (size_t i = 0; i < pixelCount; i++)
    {
        unsigned char* px = &rawData[i * 4];
        unsigned char a = px[3];
        if (a > 0 && a < 255)
        {
            int r = (px[0] * 255) / a;
            int g = (px[1] * 255) / a;
            int b = (px[2] * 255) / a;
            px[0] = (unsigned char)(r > 255 ? 255 : r);
            px[1] = (unsigned char)(g > 255 ? 255 : g);
            px[2] = (unsigned char)(b > 255 ? 255 : b);
        }
    }

    *width = (int)imgWidth;
    *height = (int)imgHeight;
    *components = 4; // ImageFormat.RGBA

    return rawData;
}

void free_image(unsigned char* image)
{
    if (image)
    {
        free(image);
    }
}

#ifdef __cplusplus
}
#endif
