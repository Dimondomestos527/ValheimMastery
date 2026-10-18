using System;
namespace ValheimMastery
{
    internal enum ResidentUniqueKind:byte { Family=0,Gesture=1 }
    internal enum ResidentUniqueOutcome:byte { Denied=0,Visual=1,HealRequested=2 }
    internal static class MasterIdolResidentUniqueRules
    {
        internal const int MaximumPending=4,MaximumReceipts=64,MaximumPacket=512;
        internal const double RequestSeconds=3,DecisionSeconds=2,ReceiptSeconds=20;
        internal const float ManualQuiet=4f;
        internal static readonly string[] GestureClips={"Wave","ChallengeAccepted","Cheer","Thumbsup","Point","Blowkiss","Bow","Flex","Laugh 2","Roar"};
        internal static readonly string[] GestureEmotes={"wave","challenge","cheer","thumbsup","point","blowkiss","bow","flex","laugh","roar"};
        private static readonly float[] GestureDurations={2.5f,3.125f,2.458333f,1.25f,2.5f,2f,3.5f,2.833333f,3.033333f,2.166667f};
        internal static int Gesture(string emote)=>Array.IndexOf(GestureEmotes,emote);
        internal static float GestureSeconds(byte choice)=>choice<GestureDurations.Length?GestureDurations[choice]:0;
        internal static bool UpperWave(int family,ResidentUniqueKind kind,byte choice)=>family==0&&kind==ResidentUniqueKind.Gesture&&choice==0;
        // Human-approved shipping subset. Family2 manual greeting reuses exact approved free-left WaveV2/50A0646E; no strength motion. Other authored motions remain held.
        // Exact human approvals: Joke0 3A803E06; Juggle0 V2 9C4473DA + retained V1 voice; Shaman0 V2 3913D3E6.
        internal static bool MotionApproved(int family,ResidentUniqueKind kind,byte choice)=>UpperWave(family,kind,choice)||(kind==ResidentUniqueKind.Family&&(family==0&&choice<10||(family==1||family==3)&&choice==0));
        internal static bool TryApprovedFamilyChoice(int family,byte start,out byte choice)
        {
            choice=0;
            if(family<0||family>3)return false;
            for(int offset=0;offset<10;offset++)
            {byte candidate=(byte)((start+offset)%10);if(MotionApproved(family,ResidentUniqueKind.Family,candidate)){choice=candidate;return true;}}
            return false;
        }
        internal static float Seconds(int family,ResidentUniqueKind kind,byte choice)
        {
            if(!MotionApproved(family,kind,choice))return 0;
            if(UpperWave(family,kind,choice))return MasterIdolResidentReactionWaveUpperBody.Seconds;
            if(kind==ResidentUniqueKind.Gesture)return GestureSeconds(choice);
            if(kind!=ResidentUniqueKind.Family)return 0;
            return family==0?3.5f:family==1?4.8f:2.266667f;
        }
        internal static bool Fresh(long stamp,long now,double seconds)
            =>stamp>0&&stamp<=DateTime.MaxValue.Ticks&&stamp<=now+TimeSpan.TicksPerSecond&&now-stamp<=seconds*TimeSpan.TicksPerSecond;
        internal static bool Nonce(string value)=>MasterIdolForestRules.Nonce(value);
        internal static string ActionName(int family)=>family==0?GoldUiLocalization.Text("Tell a joke","Розкажи жарт"):
            family==1?GoldUiLocalization.Text("Juggle stones","Пожонглюй камінцями"):
            family==2?GoldUiLocalization.Text("Wave hello","Привітайся"):
            GoldUiLocalization.Text("Healing cloud","Лікувальна хмара");
        internal static string Joke(byte index)
        {
            switch(index%10)
            {
                case 0:return GoldUiLocalization.Text("Why do vikings plant carrots? To have roots here too.","Навіщо вікінг садить моркву? Щоб теж пустити тут коріння.");
                case 1:return GoldUiLocalization.Text("The troll said he was just borrowing a log. Your roof disagreed.","Троль каже, що лише позичив колоду. Твій дах іншої думки.");
                case 2:return GoldUiLocalization.Text("I asked Hugin for a short answer. He brought another raven.","Попросив Гуґіна відповісти коротко. Він покликав ще одного крука.");
                case 3:return GoldUiLocalization.Text("My family tree says you owe us three stacks of wood.","Моє родинне дерево каже, що ти винен нам три стоси деревини.");
                case 4:return GoldUiLocalization.Text("A wooden portal cannot carry ore. A viking can carry complaints forever.","Дерев’яний портал не переносить руду. Вікінг переносить образу на це вічно.");
                case 5:return GoldUiLocalization.Text("I tried fishing. The fish asked whether I was bait or driftwood.","Пішов рибалити. Риба питає: ти наживка чи корч?");
                case 6:return GoldUiLocalization.Text("The Elder asked me to stop branching out into comedy.","Прадавній попросив мене не розгалужуватися в комедію.");
                case 7:return GoldUiLocalization.Text("Your roof needs support. I offered emotional support. It fell anyway.","Твоєму даху потрібна опора. Я підтримав його морально. Все одно впав.");
                case 8:return GoldUiLocalization.Text("I named my boat Resin. It sticks to every rock.","Назвав човен Смолою. Тепер липне до кожного каменя.");
                default:return GoldUiLocalization.Text("I am not afraid of axes. I just respect personal space. About three forests of it.","Я не боюся сокир. Просто поважаю особистий простір. Лісів на три.");
            }
        }
    }
}
