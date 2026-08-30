// Grpc.Core's internal Mono/Unix dynamic-library-loading path P/Invokes
// dlopen/dlerror/dlsym for loading the native grpc extension on Linux/macOS
// at runtime. On Windows that code path is never taken (Grpc.Core's
// UnmanagedLibrary picks the Win32 LoadLibrary path instead) - but IL2CPP
// still needs the symbols to exist to link the player, since it can't prove
// at compile time that the Unix branch is unreachable. Same fix pattern
// already used one directory up in grpc_csharp_ext_dummy_stubs.c for the
// __Internal PInvoke symbols (see the GitHub issue linked there) - these
// three are never actually called on Windows, so a dummy body is safe.

void* dlopen(const char* filename, int flags) {
  return 0;
}

char* dlerror(void) {
  return 0;
}

void* dlsym(void* handle, const char* symbol) {
  return 0;
}
