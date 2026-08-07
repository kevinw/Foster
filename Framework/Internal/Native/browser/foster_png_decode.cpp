#include "lodepng/lodepng.h"

#include <cstdlib>

extern "C" unsigned FosterPngDecodeRgba(
	const unsigned char* input,
	size_t inputSize,
	unsigned char** output,
	unsigned* width,
	unsigned* height)
{
	unsigned char* decoded = nullptr;
	auto error = lodepng_decode32(&decoded, width, height, input, inputSize);
	if (error)
	{
		*output = nullptr;
		*width = 0;
		*height = 0;
		return error;
	}

	*output = decoded;
	return 0;
}

extern "C" void FosterPngFree(void* ptr)
{
	std::free(ptr);
}
