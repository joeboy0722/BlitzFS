set(TARGET_NAME BlitzFS.Core)

# 原始碼清單
set(CORE_SOURCES
    src/CoreAPI.cpp
    src/EngineContext.cpp
    src/FlatMemoryTree.cpp
    src/MftScanner.cpp
    src/DirectoryScanner.cpp
    src/TransferEngine.cpp
    src/RingBuffer.cpp
)

set(CORE_HEADERS
    include/CommonDef.h
    include/CoreAPI.h
    include/IEngine.h
    src/EngineContext.h
    src/FlatMemoryTree.h
    src/MftScanner.h
    src/DirectoryScanner.h
    src/TransferEngine.h
    src/RingBuffer.h
)

# 建立共用動態庫 (DLL)
add_library(${TARGET_NAME} SHARED ${CORE_SOURCES} ${CORE_HEADERS})

target_include_directories(${TARGET_NAME}
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:include>
    PRIVATE
        src
)

# 定義 DLL 匯出巨集
target_compile_definitions(${TARGET_NAME} PRIVATE BLITZFS_CORE_EXPORTS)

# 連結 Windows 系統庫
target_link_libraries(${TARGET_NAME} PRIVATE
    kernel32
    user32
    advapi32
    shell32
)
