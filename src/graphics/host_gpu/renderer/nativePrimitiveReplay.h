// Copyright (C) 2026 Prosperismo Emulator Project
// SPDX-License-Identifier: GPL-2.0-or-later

#pragma once

#include <cstdint>
#include <limits>

namespace Libs::Graphics {

enum class NativePrimitiveOutput : uint32_t { Points = 0, Lines = 1, Triangles = 2 };

struct NativePrimitiveLaunchState {
	uint32_t shader_stages             = 0;
	uint32_t primitive_group_size      = 0;
	uint32_t vertex_group_size         = 0;
	uint32_t primitive_amplification   = 0;
	uint32_t max_output_per_subgroup   = 0;
	uint32_t gs_max_vertices_per_input = 0;
	uint32_t gs_output_primitive       = 0;
	uint32_t gs_instance_count         = 0;
	uint32_t esgs_ring_item_size       = 0;
	uint32_t ge_user_vgpr_enable       = 0;
};

struct ClassicGeometryReplayLaunch {
	uint32_t wave_lane_count              = 0;
	uint32_t input_primitives_per_subgroup = 0;
	uint32_t input_vertices_per_subgroup   = 0;
	uint32_t output_vertex_slots           = 0;
	uint32_t output_primitive_slots        = 0;
	NativePrimitiveOutput output_primitive = NativePrimitiveOutput::Points;
};

// Sony SDK 10's CxVgtShaderStagesEn names GS_EN, HS_EN, PASSTHROUGH and the
// geometry wave-size bit.  A classic GS uses GS_EN without PASSTHROUGH.  SDK
// native_prim.pssl supplies the subgroup-relative output ABI; Sony's register
// NATVIS supplies GE_CNTL and output-limit field widths.  PRIM_AMP_FACTOR is
// the only field below corroborated by the gfx10.1 register table because SDK
// 10 does not expose that register in NATVIS.
[[nodiscard]] constexpr bool TryCreateClassicGeometryReplayLaunch(
	const NativePrimitiveLaunchState& state, ClassicGeometryReplayLaunch& launch) {
	constexpr uint32_t HsEnableMask      = 0x00000004u;
	constexpr uint32_t GsEnableMask      = 0x00000020u;
	constexpr uint32_t GsWave32Mask      = 0x00400000u;
	constexpr uint32_t PassthroughMask   = 0x02000000u;
	constexpr uint32_t MaximumGroupSize  = 0x100u;
	constexpr uint32_t MaximumOutput     = 0x3ffu;

	launch = {};
	if ((state.shader_stages & GsEnableMask) == 0 ||
	    (state.shader_stages & (HsEnableMask | PassthroughMask)) != 0 ||
	    (state.shader_stages & GsWave32Mask) != 0 ||
	    state.primitive_group_size == 0 ||
	    state.primitive_group_size > MaximumGroupSize ||
	    state.vertex_group_size == 0 || state.vertex_group_size > MaximumGroupSize ||
	    state.primitive_amplification == 0 ||
	    state.max_output_per_subgroup == 0 ||
	    state.max_output_per_subgroup > MaximumOutput ||
	    state.gs_max_vertices_per_input == 0 ||
	    state.gs_max_vertices_per_input > 0x7ffu ||
	    state.gs_instance_count != 0 || state.esgs_ring_item_size == 0 ||
	    state.ge_user_vgpr_enable != 0 ||
	    state.gs_output_primitive > static_cast<uint32_t>(NativePrimitiveOutput::Triangles)) {
		return false;
	}

	const auto vertex_slots = static_cast<uint64_t>(state.primitive_group_size) *
	                          state.gs_max_vertices_per_input;
	if (vertex_slots != state.max_output_per_subgroup) {
		return false;
	}

	const auto output = static_cast<NativePrimitiveOutput>(state.gs_output_primitive);
	uint32_t primitive_limit_per_input = state.gs_max_vertices_per_input;
	if (output == NativePrimitiveOutput::Lines) {
		if (primitive_limit_per_input < 2) {
			return false;
		}
		primitive_limit_per_input -= 1;
	} else if (output == NativePrimitiveOutput::Triangles) {
		if (primitive_limit_per_input < 3) {
			return false;
		}
		primitive_limit_per_input -= 2;
	}
	if (state.primitive_amplification > primitive_limit_per_input) {
		return false;
	}

	const auto primitive_slots = static_cast<uint64_t>(state.primitive_group_size) *
	                             state.primitive_amplification;
	if (primitive_slots > std::numeric_limits<uint32_t>::max()) {
		return false;
	}

	launch.wave_lane_count               = 64;
	launch.input_primitives_per_subgroup = state.primitive_group_size;
	launch.input_vertices_per_subgroup   = state.vertex_group_size;
	launch.output_vertex_slots           = state.max_output_per_subgroup;
	launch.output_primitive_slots        = static_cast<uint32_t>(primitive_slots);
	launch.output_primitive              = output;
	return true;
}

} // namespace Libs::Graphics
