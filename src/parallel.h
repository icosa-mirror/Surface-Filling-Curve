// MIT License
// Copyright 2026 Giovanni Cocco and Inria

#pragma once
#include <functional>
#include <array>
#include <atomic>

namespace Parallel {
    void For(size_t start, size_t stop, std::function<void(size_t)> foo, const std::atomic<int>* cancel = nullptr);
    bool ForAny(size_t start, size_t stop, std::function<bool(size_t)> foo, const std::atomic<int>* cancel = nullptr);
    std::array<bool, 2> ForAny2(size_t start, size_t stop, std::function<std::array<bool, 2>(size_t)> foo, const std::atomic<int>* cancel = nullptr);
    size_t ArgMin(size_t start, size_t stop, std::function<float(size_t)> foo, const std::atomic<int>* cancel = nullptr);
}