#include "kernel/eventFlag.h"
#include "kernel/uuid.h"
#include "libs/errno.h"
#include "libs/systemService.h"

#include <array>
#include <bit>
#include <cstdint>
#include <cstdio>
#include <cstdlib>

namespace {

void Check(bool value, const char* text) {
	if (!value) {
		std::fprintf(stderr, "Prosperismo HLE contract test failed: %s\n", text);
		std::abort();
	}
}

uint16_t ClockSequence(const Libs::LibKernel::KernelUuid& uuid) {
	return static_cast<uint16_t>(((uuid.clock_seq_hi_and_reserved & 0x3fu) << 8u) |
	                             uuid.clock_seq_low);
}

void TestFirmwareHdrFallback() {
	Libs::SystemService::SystemServiceHdrToneMapLuminance luminance {};
	Libs::SystemService::PopulateHdrToneMapLuminance(&luminance);

	Check(std::bit_cast<uint32_t>(luminance.max_full_frame_tone_map_luminance) == 0x441f546au,
	      "full-frame luminance differs from firmware fallback");
	Check(std::bit_cast<uint32_t>(luminance.max_tone_map_luminance) == 0x44754958u,
	      "maximum luminance differs from firmware fallback");
	Check(std::bit_cast<uint32_t>(luminance.min_tone_map_luminance) == 0x3dffddecu,
	      "minimum luminance differs from firmware fallback");
}

void TestUuidVersionOneLayout() {
	constexpr uint64_t timestamp = 0x0123'4567'89ab'cdefull;
	Libs::LibKernel::KernelUuidGenerator generator({0x02, 0x11, 0x22, 0x33, 0x44, 0x55}, 0x1234);

	const auto first = generator.GenerateAtTimestamp(timestamp);
	Check(first.time_low == 0x89abcdefu, "UUID time-low field is wrong");
	Check(first.time_mid == 0x4567u, "UUID time-mid field is wrong");
	Check(first.time_hi_and_version == 0x1123u, "UUID is not a version-1 identifier");
	Check((first.clock_seq_hi_and_reserved & 0xc0u) == 0x80u,
	      "UUID does not carry the RFC 4122 variant");
	Check((first.node[0] & 0x01u) != 0, "random UUID node lacks multicast marker");
	Check(ClockSequence(first) == 0x1234u, "UUID clock sequence is wrong");

	const auto second = generator.GenerateAtTimestamp(timestamp);
	Check(ClockSequence(second) == 0x1235u,
	      "UUID clock sequence did not advance when the clock repeated");

	Check(Libs::LibKernel::KernelUuidCreate(nullptr) == Libs::LibKernel::KERNEL_ERROR_EINVAL,
	      "UUID null-output contract is wrong");
	Libs::LibKernel::KernelUuid generated {};
	Check(Libs::LibKernel::KernelUuidCreate(&generated) == OK, "UUID creation failed");
	Check((generated.time_hi_and_version & 0xf000u) == 0x1000u,
	      "runtime UUID is not version 1");
	Check((generated.clock_seq_hi_and_reserved & 0xc0u) == 0x80u,
	      "runtime UUID variant is wrong");
}

void TestNamedEventFlagLifetimeAndExtendedAttribute() {
	using namespace Libs::LibKernel::EventFlag;
	constexpr char name[] = "prosperismo-hle-contract";

	KernelEventFlag created = nullptr;
	Check(KernelCreateEventFlag(&created, name, 0x120u, 0, nullptr) == OK,
	      "firmware-used event-flag attribute 0x100 was rejected");
	Check(created != nullptr, "event flag create returned a null handle");

	KernelEventFlag opened = nullptr;
	Check(KernelOpenEventFlag(&opened, name) == OK, "named event flag did not open");
	Check(opened == created, "named event flag open fabricated a private object");
	Check(KernelCloseEventFlag(opened) == OK, "opened event flag did not close");
	Check(KernelSetEventFlag(created, 1) == OK,
	      "closing one reference destroyed the shared event flag");
	Check(KernelCloseEventFlag(created) == OK, "creator event flag did not close");

	opened = nullptr;
	Check(KernelOpenEventFlag(&opened, name) == Libs::LibKernel::KERNEL_ERROR_ESRCH,
	      "closed event flag remained published by name");

	Check(KernelCreateEventFlag(&created, name, 0x20u, 0, nullptr) == OK,
	      "event flag name could not be reused after last close");
	Check(KernelDeleteEventFlag(created) == OK, "event flag cleanup failed");
}

} // namespace

int main() {
	TestFirmwareHdrFallback();
	TestUuidVersionOneLayout();
	TestNamedEventFlagLifetimeAndExtendedAttribute();
	return 0;
}
