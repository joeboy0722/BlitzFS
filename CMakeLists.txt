cmake_minimum_required(VERSION 3.24)
project(BlitzFS LANGUAGES CXX C)

# 設定 C++20 標準
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# 輸出目錄設定
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_LIBRARY_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/bin)
set(CMAKE_ARCHIVE_OUTPUT_DIRECTORY ${CMAKE_BINARY_DIR}/lib)

# MSVC 編譯最佳化選項
if (MSVC)
    add_compile_options(
        /W4          # 警告等級 4
        /utf-8       # 強制 UTF-8 編碼
        /permissive- # 嚴格遵循標準
        /MP          # 啟用多處理器編譯
    )
    # Release 模式下的極限最佳化
    add_compile_options("$<$<CONFIG:Release>:/O2;/Oi;/Ot;/arch:AVX2>")
endif()

# 引入子模組
add_subdirectory(src/BlitzFS.Core)
add_subdirectory(tests/BlitzFS.Core.Tests)
