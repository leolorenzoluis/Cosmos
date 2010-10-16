﻿// uncomment the next line to enable LFB access, for now hardcoded at 1024x768x8b
//#define LFB_1024_8
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CPU = Cosmos.Compiler.Assembler.X86;
using Cosmos.IL2CPU.X86;
using System.Reflection;
using Cosmos.IL2CPU.ILOpCodes;
using Cosmos.Compiler.Assembler;
using Cosmos.Compiler.Assembler.X86;
using Cosmos.Compiler.DebugStub;
using Cosmos.Compiler.XSharp;

namespace Cosmos.IL2CPU.X86
{
    // TODO: I think we need to later elminate this class
    // Much of it is left over from the old build stuff, and info 
    // here actually belongs else where, not in the assembler
    public class CosmosAssembler : Cosmos.Compiler.Assembler.Assembler
    {
        //TODO: COM Port info - should be in assembler? Assembler should not know about comports...
        protected byte mComNumber = 0;

        public CosmosAssembler(byte aComNumber) {
            mComNumber = aComNumber;
        }

        private static string GetValidGroupName(string aGroup)
        {
            return aGroup.Replace('-', '_').Replace('.', '_');
        }
        public const string EntryPointName = "__ENGINE_ENTRYPOINT__";
        public override void Initialize()
        {
            base.Initialize();
            if (mComNumber > 0)
            {
                new Define("DEBUGSTUB");
            }
            new Label("Kernel_Start") { IsGlobal = true };
            
            // CLI ASAP
            new ClrInterruptFlag();

            new Comment(this, "MultiBoot-compliant loader (e.g. GRUB or X.exe) provides info in registers: ");
            new Comment(this, "EBX=multiboot_info ");
            new Comment(this, "EAX=0x2BADB002 - check if it's really Multiboot loader ");
            new Comment(this, "                ;- copy mb info - some stuff for you  ");
            //new Move { DestinationReg = Registers.ESI, SourceReg = Registers.EBX };
            //new Move { DestinationReg = Registers.EDI, SourceRef = ElementReference.New("MultiBootInfo_Structure") };
            //new Move { DestinationReg = Registers.ECX, SourceValue = 0x58 };
            //new Movs { Prefixes = InstructionPrefixes.Repeat, Size = 8 };

            //new Move { DestinationReg = Registers.EBX, SourceRef = ElementReference.New("MultiBootInfo_Structure") };
            new Move { DestinationRef = ElementReference.New("MultiBootInfo_Structure"), DestinationIsIndirect = true, SourceReg = Registers.EBX };
            new Add { DestinationReg = Registers.EBX, SourceValue = 4 };
            new Move
            {
                DestinationReg = Registers.EAX,
                SourceReg = Registers.EBX,
                SourceIsIndirect = true
            };
            new Move { DestinationRef = ElementReference.New("MultiBootInfo_Memory_Low"), DestinationIsIndirect = true, SourceReg = Registers.EAX };
            new Add { DestinationReg = Registers.EBX, SourceValue = 4 };
            new Move
            {
                DestinationReg = Registers.EAX,
                SourceReg = Registers.EBX,
                SourceIsIndirect = true
            };
            new Move { DestinationRef = ElementReference.New("MultiBootInfo_Memory_High"), DestinationIsIndirect = true, SourceReg = Registers.EAX };
            new Move
            {
                DestinationReg = Registers.ESP,
                SourceRef = ElementReference.New("Kernel_Stack")
            };
#if LFB_1024_8
            new Comment("Set graphics fields");
            new Move { DestinationReg = Registers.EBX, SourceRef = ElementReference.New("MultiBootInfo_Structure"), SourceIsIndirect = true };
            new Move { DestinationReg = Registers.EAX, SourceReg = Registers.EBX, SourceIsIndirect = true, SourceDisplacement = 72 };
            new Move { DestinationRef = ElementReference.New("MultibootGraphicsRuntime_VbeControlInfoAddr"), DestinationIsIndirect = true, SourceReg = Registers.EAX };
            new Move { DestinationReg = Registers.EAX, SourceReg = Registers.EBX, SourceIsIndirect = true, SourceDisplacement = 76 };
            new Move { DestinationRef = ElementReference.New("MultibootGraphicsRuntime_VbeModeInfoAddr"), DestinationIsIndirect = true, SourceReg = Registers.EAX };
            new Move { DestinationReg = Registers.EAX, SourceReg = Registers.EBX, SourceIsIndirect = true, SourceDisplacement = 80 };
            new Move { DestinationRef = ElementReference.New("MultibootGraphicsRuntime_VbeMode"), DestinationIsIndirect = true, SourceReg = Registers.EAX };
#endif
            new Comment(this, "some more startups todo");

            // SSE init
            // CR4[bit 9]=1, CR4[bit 10]=1, CR0[bit 2]=0, CR0[bit 1]=1
            new Move { DestinationReg = Registers.EAX, SourceReg = Registers.CR4 };
            new Or { DestinationReg = Registers.EAX, SourceValue = 0x100 };
            new Move { DestinationReg = Registers.CR4, SourceReg = Registers.EAX };
            new Move { DestinationReg = Registers.EAX, SourceReg = Registers.CR4 };
            new Or { DestinationReg = Registers.EAX, SourceValue = 0x200 };
            new Move { DestinationReg = Registers.CR4, SourceReg = Registers.EAX };
            new Move { DestinationReg = Registers.EAX, SourceReg = Registers.CR0 };

            new And { DestinationReg = Registers.EAX, SourceValue = 0xfffffffd };
            new Move { DestinationReg = Registers.CR0, SourceReg = Registers.EAX };
            new Move { DestinationReg = Registers.EAX, SourceReg = Registers.CR0 };

            new And { DestinationReg = Registers.EAX, SourceValue = 1 };
            new Move { DestinationReg = Registers.CR0, SourceReg = Registers.EAX };
            // END SSE INIT

            if (mComNumber > 0)
            {
                CodeBlock.Call<DebugStub.Init>();
            }
            else
            {
                DataMembers.Add(new DataMember("InterruptsEnabledFlag", new int[] { 0 }));
            }

            // Jump to Kernel entry point
            new Call { DestinationLabel = EntryPointName };

            // After Kernel is done, sit here and halt till next IRQ.
            new Label(".loop");
                new ClrInterruptFlag();
                new Halt();
            new Jump { DestinationLabel = ".loop" };

            if (mComNumber > 0) {
                var xStub = new DebugStub(mComNumber);
                xStub.Assemble();

                UInt16[] xComPortAddresses = { 0x3F8, 0x2F8, 0x3E8, 0x2E8 };
                var xStubOld = new DebugStubOld();
                xStubOld.Main(xComPortAddresses[mComNumber - 1]);
            } else {
                new Label("DebugStub_Step");
                new Return();
            }

#if !LFB_1024_8
            DataMembers.Add(new DataIfNotDefined("ELF_COMPILATION"));
            uint xFlags = 0x10003;
            DataMembers.Add(new DataMember("MultibootSignature",
                                   new uint[] { 0x1BADB002 }));
            DataMembers.Add(new DataMember("MultibootFlags",
                           xFlags));
            DataMembers.Add(new DataMember("MultibootChecksum",
                                               (int)(0 - (xFlags + 0x1BADB002))));
            DataMembers.Add(new DataMember("MultibootHeaderAddr", ElementReference.New("MultibootSignature")));
            DataMembers.Add(new DataMember("MultibootLoadAddr", ElementReference.New("MultibootSignature")));
            DataMembers.Add(new DataMember("MultibootLoadEndAddr", ElementReference.New("_end_code")));
            DataMembers.Add(new DataMember("MultibootBSSEndAddr", ElementReference.New("_end_code")));
            DataMembers.Add(new DataMember("MultibootEntryAddr", ElementReference.New("Kernel_Start")));
            DataMembers.Add(new DataEndIfDefined());
            DataMembers.Add(new DataIfDefined("ELF_COMPILATION"));                                                    
            xFlags = 0x00003;
            DataMembers.Add(new DataMember("MultibootSignature",
                                   new uint[] { 0x1BADB002 }));
            DataMembers.Add(new DataMember("MultibootFlags",
                           xFlags));
            DataMembers.Add(new DataMember("MultibootChecksum",
                                               (int)(0 - (xFlags + 0x1BADB002))));
            DataMembers.Add(new DataEndIfDefined());
#else
            DataMembers.Add(new DataIfNotDefined("ELF_COMPILATION"));
            uint xFlags = 0x10007;
            DataMembers.Add(new DataMember("MultibootSignature",
                                   new uint[] { 0x1BADB002 }));
            DataMembers.Add(new DataMember("MultibootFlags",
                           xFlags));
            DataMembers.Add(new DataMember("MultibootChecksum",
                                               (int)(0 - (xFlags + 0x1BADB002))));
            DataMembers.Add(new DataMember("MultibootHeaderAddr", ElementReference.New("MultibootSignature")));
            DataMembers.Add(new DataMember("MultibootLoadAddr", ElementReference.New("MultibootSignature")));
            DataMembers.Add(new DataMember("MultibootLoadEndAddr", ElementReference.New("_end_code")));
            DataMembers.Add(new DataMember("MultibootBSSEndAddr", ElementReference.New("_end_code")));
            DataMembers.Add(new DataMember("MultibootEntryAddr", ElementReference.New("Kernel_Start")));
            // graphics fields
            DataMembers.Add(new DataMember("MultibootGraphicsMode", 0));
            DataMembers.Add(new DataMember("MultibootGraphicsWidth", 1024));
            DataMembers.Add(new DataMember("MultibootGraphicsHeight", 768));
            DataMembers.Add(new DataMember("MultibootGraphicsDepth", 8));
            DataMembers.Add(new DataEndIfDefined());
            DataMembers.Add(new DataIfDefined("ELF_COMPILATION"));
            xFlags = 0x00003;
            DataMembers.Add(new DataMember("MultibootSignature",
                                   new uint[] { 0x1BADB002 }));
            DataMembers.Add(new DataMember("MultibootFlags",
                           xFlags));
            DataMembers.Add(new DataMember("MultibootChecksum",
                                               (int)(0 - (xFlags + 0x1BADB002))));
            DataMembers.Add(new DataEndIfDefined());

#endif
            // graphics info fields 
            DataMembers.Add(new DataMember("MultibootGraphicsRuntime_VbeModeInfoAddr", Int32.MaxValue));
            DataMembers.Add(new DataMember("MultibootGraphicsRuntime_VbeControlInfoAddr", Int32.MaxValue));
            DataMembers.Add(new DataMember("MultibootGraphicsRuntime_VbeMode", Int32.MaxValue));
            // memory
            DataMembers.Add(new DataMember("MultiBootInfo_Memory_High", 0));
            DataMembers.Add(new DataMember("MultiBootInfo_Memory_Low", 0));
            DataMembers.Add(new DataMember("Before_Kernel_Stack",
                           new byte[0x50000]));
            DataMembers.Add(new DataMember("Kernel_Stack",
                           new byte[0]));
            DataMembers.Add(new DataMember("MultiBootInfo_Structure", new uint[1]));
        }

        protected override void OnBeforeFlush()
        {
            base.OnBeforeFlush();
            DataMembers.AddRange(new DataMember[]{
                    new DataMember("_end_data",
                                   new byte[0])});
            new Label("_end_code");
        }

        public override void FlushText(TextWriter aOutput)
        {
            base.FlushText(aOutput);
        }
    }
}