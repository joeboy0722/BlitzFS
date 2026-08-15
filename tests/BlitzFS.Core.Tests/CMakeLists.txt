set(TEST_TARGET_NAME BlitzFS.Core.Tests)

set(TEST_SOURCES
    main.cpp
)

add_executable(${TEST_TARGET_NAME} ${TEST_SOURCES})

target_include_directories(${TEST_TARGET_NAME} PRIVATE
    ../../src/BlitzFS.Core/include
)

target_link_libraries(${TEST_TARGET_NAME} PRIVATE
    BlitzFS.Core
)
