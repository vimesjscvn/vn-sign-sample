// PKCS#11 C bridge header for Swift interop
#ifndef PKCS11_BRIDGE_H
#define PKCS11_BRIDGE_H

#include <stdint.h>
#include <stdlib.h>
#include <dlfcn.h>

typedef unsigned long CK_ULONG;
typedef unsigned char CK_BYTE;
typedef CK_BYTE CK_BBOOL;
typedef CK_ULONG CK_RV;
typedef CK_ULONG CK_SLOT_ID;
typedef CK_ULONG CK_SESSION_HANDLE;
typedef CK_ULONG CK_OBJECT_HANDLE;
typedef CK_ULONG CK_MECHANISM_TYPE;
typedef CK_ULONG CK_ATTRIBUTE_TYPE;
typedef CK_ULONG CK_OBJECT_CLASS;
typedef CK_ULONG CK_KEY_TYPE;
typedef CK_ULONG CK_USER_TYPE;
typedef CK_ULONG CK_FLAGS;
typedef void *CK_NOTIFY;

#define CKR_OK                  0x00000000UL
#define CKR_USER_ALREADY_LOGGED_IN 0x00000100UL
#define CKO_CERTIFICATE         0x00000001UL
#define CKO_PRIVATE_KEY         0x00000003UL
#define CKA_CLASS               0x00000000UL
#define CKA_VALUE               0x00000011UL
#define CKA_ID                  0x00000102UL
#define CKA_KEY_TYPE            0x00000100UL
#define CKK_RSA                 0x00000000UL
#define CKK_EC                  0x00000003UL
#define CKM_RSA_PKCS            0x00000001UL
#define CKM_ECDSA               0x00001041UL
#define CKU_USER                1UL
#define CKF_SERIAL_SESSION      0x00000004UL
#define CK_INVALID_HANDLE       0UL

typedef struct {
    CK_MECHANISM_TYPE mechanism;
    void *pParameter;
    CK_ULONG ulParameterLen;
} CK_MECHANISM;

typedef struct {
    CK_ATTRIBUTE_TYPE type;
    void *pValue;
    CK_ULONG ulValueLen;
} CK_ATTRIBUTE;

// Function pointer types
typedef CK_RV (*CK_C_Initialize)(void *);
typedef CK_RV (*CK_C_Finalize)(void *);
typedef CK_RV (*CK_C_GetSlotList)(CK_BBOOL, CK_SLOT_ID *, CK_ULONG *);
typedef CK_RV (*CK_C_OpenSession)(CK_SLOT_ID, CK_FLAGS, void *, CK_NOTIFY, CK_SESSION_HANDLE *);
typedef CK_RV (*CK_C_CloseSession)(CK_SESSION_HANDLE);
typedef CK_RV (*CK_C_Login)(CK_SESSION_HANDLE, CK_USER_TYPE, CK_BYTE *, CK_ULONG);
typedef CK_RV (*CK_C_Logout)(CK_SESSION_HANDLE);
typedef CK_RV (*CK_C_FindObjectsInit)(CK_SESSION_HANDLE, CK_ATTRIBUTE *, CK_ULONG);
typedef CK_RV (*CK_C_FindObjects)(CK_SESSION_HANDLE, CK_OBJECT_HANDLE *, CK_ULONG, CK_ULONG *);
typedef CK_RV (*CK_C_FindObjectsFinal)(CK_SESSION_HANDLE);
typedef CK_RV (*CK_C_GetAttributeValue)(CK_SESSION_HANDLE, CK_OBJECT_HANDLE, CK_ATTRIBUTE *, CK_ULONG);
typedef CK_RV (*CK_C_SignInit)(CK_SESSION_HANDLE, CK_MECHANISM *, CK_OBJECT_HANDLE);
typedef CK_RV (*CK_C_Sign)(CK_SESSION_HANDLE, CK_BYTE *, CK_ULONG, CK_BYTE *, CK_ULONG *);

// Wrapper struct
typedef struct {
    void *handle;
    CK_C_Initialize         C_Initialize;
    CK_C_Finalize           C_Finalize;
    CK_C_GetSlotList        C_GetSlotList;
    CK_C_OpenSession        C_OpenSession;
    CK_C_CloseSession       C_CloseSession;
    CK_C_Login              C_Login;
    CK_C_Logout             C_Logout;
    CK_C_FindObjectsInit    C_FindObjectsInit;
    CK_C_FindObjects        C_FindObjects;
    CK_C_FindObjectsFinal   C_FindObjectsFinal;
    CK_C_GetAttributeValue  C_GetAttributeValue;
    CK_C_SignInit            C_SignInit;
    CK_C_Sign               C_Sign;
} Pkcs11Lib;

static inline Pkcs11Lib *pkcs11_load(const char *path) {
    void *h = dlopen(path, RTLD_LAZY);
    if (!h) return NULL;

    Pkcs11Lib *lib = (Pkcs11Lib *)calloc(1, sizeof(Pkcs11Lib));
    lib->handle = h;
    lib->C_Initialize       = (CK_C_Initialize)dlsym(h, "C_Initialize");
    lib->C_Finalize         = (CK_C_Finalize)dlsym(h, "C_Finalize");
    lib->C_GetSlotList      = (CK_C_GetSlotList)dlsym(h, "C_GetSlotList");
    lib->C_OpenSession      = (CK_C_OpenSession)dlsym(h, "C_OpenSession");
    lib->C_CloseSession     = (CK_C_CloseSession)dlsym(h, "C_CloseSession");
    lib->C_Login            = (CK_C_Login)dlsym(h, "C_Login");
    lib->C_Logout           = (CK_C_Logout)dlsym(h, "C_Logout");
    lib->C_FindObjectsInit  = (CK_C_FindObjectsInit)dlsym(h, "C_FindObjectsInit");
    lib->C_FindObjects      = (CK_C_FindObjects)dlsym(h, "C_FindObjects");
    lib->C_FindObjectsFinal = (CK_C_FindObjectsFinal)dlsym(h, "C_FindObjectsFinal");
    lib->C_GetAttributeValue = (CK_C_GetAttributeValue)dlsym(h, "C_GetAttributeValue");
    lib->C_SignInit          = (CK_C_SignInit)dlsym(h, "C_SignInit");
    lib->C_Sign              = (CK_C_Sign)dlsym(h, "C_Sign");

    if (!lib->C_Initialize || !lib->C_GetSlotList || !lib->C_OpenSession ||
        !lib->C_FindObjectsInit || !lib->C_FindObjects || !lib->C_Sign) {
        free(lib);
        dlclose(h);
        return NULL;
    }

    return lib;
}

static inline void pkcs11_free(Pkcs11Lib *lib) {
    if (lib) {
        if (lib->C_Finalize) lib->C_Finalize(NULL);
        if (lib->handle) dlclose(lib->handle);
        free(lib);
    }
}

#endif
