// Copyright (C) 2026 Prosperismo Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

#include "graphics/host_gpu/renderer/nativePrimitiveReplay.h"

#include <cstdio>
#include <cstdlib>

using Libs::Graphics::ClassicGeometryReplayLaunch;
using Libs::Graphics::NativePrimitiveLaunchState;
using Libs::Graphics::NativePrimitiveOutput;
using Libs::Graphics::TryCreateClassicGeometryReplayLaunch;

namespace {

void Check(bool condition, const char* message) {
	if (!condition) {
		std::fprintf(stderr, "NativePrimitiveReplayTests: %s\n", message);
		std::exit(1);
	}
}

NativePrimitiveLaunchState CapturedClassicGsState() {
	return {
	    .shader_stages             = 0x00002030,
	    .primitive_group_size      = 3,
	    .vertex_group_size         = 24,
	    .primitive_amplification   = 70,
	    .max_output_per_subgroup   = 216,
	    .gs_max_vertices_per_input = 72,
	    .gs_output_primitive       = 2,
	    .gs_instance_count         = 0,
	    .esgs_ring_item_size       = 4,
	    .ge_user_vgpr_enable       = 0,
	};
}

} // namespace

int main() {
	ClassicGeometryReplayLaunch launch {};
	auto state = CapturedClassicGsState();
	Check(TryCreateClassicGeometryReplayLaunch(state, launch),
	      "captured classic-GS launch was rejected");
	Check(launch.wave_lane_count == 64 && launch.input_primitives_per_subgroup == 3 &&
	          launch.input_vertices_per_subgroup == 24 && launch.output_vertex_slots == 216 &&
	          launch.output_primitive_slots == 210 &&
	          launch.output_primitive == NativePrimitiveOutput::Triangles,
	      "captured launch was decoded incorrectly");

	auto reject = [&](const NativePrimitiveLaunchState& candidate, const char* message) {
		ClassicGeometryReplayLaunch rejected {};
		Check(!TryCreateClassicGeometryReplayLaunch(candidate, rejected), message);
	};

	auto candidate = state;
	candidate.shader_stages &= ~0x20u;
	reject(candidate, "draw without GS_EN was admitted");
	candidate = state;
	candidate.shader_stages |= 0x4u;
	reject(candidate, "tessellated launch was admitted without HS semantics");
	candidate = state;
	candidate.shader_stages |= 0x02000000;
	reject(candidate, "NGG passthrough was admitted as classic GS");
	candidate = state;
	candidate.shader_stages |= 0x00400000;
	reject(candidate, "wave32 launch was admitted to the wave64 contract");
	candidate = state;
	candidate.max_output_per_subgroup--;
	reject(candidate, "inconsistent output vertex budget was admitted");
	candidate = state;
	candidate.primitive_amplification++;
	reject(candidate, "triangle-strip primitive budget overflow was admitted");
	candidate = state;
	candidate.gs_instance_count = 1;
	reject(candidate, "GS instancing was admitted without launch semantics");
	candidate = state;
	candidate.ge_user_vgpr_enable = 1;
	reject(candidate, "user launch VGPRs were admitted without ABI semantics");
	candidate = state;
	candidate.esgs_ring_item_size = 0;
	reject(candidate, "missing ES/GS ring contract was admitted");
	candidate = state;
	candidate.vertex_group_size = 257;
	reject(candidate, "out-of-range Sony GE_CNTL vertex group was admitted");
	candidate = state;
	candidate.gs_output_primitive = 3;
	reject(candidate, "unsupported rectangle output was admitted");

	candidate = state;
	candidate.gs_output_primitive = 1;
	candidate.primitive_amplification = 71;
	Check(TryCreateClassicGeometryReplayLaunch(candidate, launch) &&
	          launch.output_primitive == NativePrimitiveOutput::Lines &&
	          launch.output_primitive_slots == 213,
	      "general line-strip ceiling was not admitted");
	candidate.gs_output_primitive = 0;
	candidate.primitive_amplification = 72;
	Check(TryCreateClassicGeometryReplayLaunch(candidate, launch) &&
	          launch.output_primitive == NativePrimitiveOutput::Points &&
	          launch.output_primitive_slots == 216,
	      "general point-list ceiling was not admitted");

	std::puts("NativePrimitiveReplayTests: ok");
	return 0;
}
