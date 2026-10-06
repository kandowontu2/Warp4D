using System.Text.Json;
namespace Warp4D;
internal static class IcarusTrainingControllerTests
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try
        {
            int checks=0;
            void Check(bool value){if(!value)throw new InvalidOperationException("Training policy check"+checks);checks++;}
            byte[] ram=new byte[2048],oam=new byte[256];
            for(int i=0;i<64;i++)oam[i*4]=249;
            ram[160]=2;ram[304]=2;ram[59]=35;ram[58]=8;ram[166]=7;ram[1827]=128;ram[1824]=186;
            var policy=new IcarusTrainingController();
            Check(policy.Choose(ram,oam).Mask==64);
            ram[58]=9;Check(policy.Choose(ram,oam).Mask==2);
            var entryPolicy=new IcarusTrainingController();ram[1827]=200;ram[1824]=154;
            Check(entryPolicy.Choose(ram,oam).Mask==66);
            ram[1827]=136;ram[1824]=186;Check(entryPolicy.Choose(ram,oam).Mask==2);
            ram[1827]=128;
            ram[1824]=202;Check(policy.Choose(ram,oam).Mask==2);
            ram[1827]=184;Check((policy.Choose(ram,oam).Mask&16)==0);
            ram[1827]=128;ram[1824]=186;Check((policy.Choose(ram,oam).Mask&16)==0);
            oam[32]=170;oam[34]=1;oam[35]=128;
            Check(policy.Choose(ram,oam).Hazards==0);oam[32]=249;
            oam[64]=178;oam[66]=2;oam[67]=124;
            var dodge=policy.Choose(ram,oam);Check(dodge.Hazards==1&&dodge.Target!=128&&(dodge.Mask&192)!=0);
            oam[64]=249;ram[1827]=8;Check((policy.Choose(ram,oam).Mask&128)!=0);
            ram[1827]=184;Check((policy.Choose(ram,oam).Mask&64)!=0);
            ram[1827]=128;
            var timePolicy=new IcarusTrainingController();
            oam[64]=110;oam[66]=2;oam[67]=124;
            timePolicy.Choose(ram,oam,100);
            oam[64]=118;
            var predictive=timePolicy.Choose(ram,oam,104);
            Check(predictive.Hazards==1 && (predictive.Mask&192)!=0);
            Check(Math.Abs(predictive.Target-128)<=32 && predictive.Target<=176);
            oam[64]=249;
            ram[1827]=240;Check((policy.Choose(ram,oam).Mask&64)!=0);
            ram[166]=0;Check(policy.Choose(ram,oam).Mask==0);
            ram[166]=7;ram[58]=0;Check(policy.Choose(ram,oam).Mask==0);
            try{policy.Choose(ram,[]);throw new Exception("Short OAM accepted");}catch(InvalidDataException){checks++;}
            ram[59]=37;try{policy.Choose(ram,oam);throw new Exception("Market accepted");}catch(InvalidDataException){checks++;}
            foreach(var bad in new ProfileCalibration.Step[]{
                new(){IcarusTrainingDodgeMilliseconds=999},new(){IcarusTrainingDodgeMilliseconds=60001},
                new(){IcarusTrainingDodgeMilliseconds=1000,RamWrites=new(){[166]=15}},
                new(){IcarusTrainingDodgeMilliseconds=1000,WorkRamWrites=new(){[0]=1}},
                new(){IcarusTrainingDodgeMilliseconds=1000,LoadStateSlot=1},
                new(){IcarusTrainingDodgeMilliseconds=1000,IcarusDoorIndex=12},
                new(){IcarusTrainingDodgeMilliseconds=1000,Mask=2},
                new(){IcarusTrainingDodgeMilliseconds=1000,Reset=true}})
            {try{ProfileCalibration.ValidateTrainingStep(bad);throw new Exception("Unsafe feedback step accepted");}catch(InvalidDataException){checks++;}}
            ProfileCalibration.ValidateTrainingStep(new(){IcarusTrainingDodgeMilliseconds=60000});checks++;
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{Passed=true,Checks=checks,Scope="Controller policy and pre-load request guards only; not native survival or reward."}));return 0;
        }
        catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());return 1;}
    }
}
