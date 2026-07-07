.686p
.xmm
.model flat, C
option casemap:none

EXTERN C AgentNativeHookHandler:PROC

.code

AgentNativeHookBridge PROC C
    push ebp
    mov ebp, esp
    sub esp, 528
    mov eax, esp
    add eax, 15
    and eax, 0FFFFFFF0h
    fxsave [eax]
    push eax
    push DWORD PTR [ebp+12]
    push DWORD PTR [ebp+8]
    call AgentNativeHookHandler
    add esp, 8
    mov ecx, eax
    pop edx
    fxrstor [edx]
    mov eax, ecx
    mov esp, ebp
    pop ebp
    ret
AgentNativeHookBridge ENDP

END
