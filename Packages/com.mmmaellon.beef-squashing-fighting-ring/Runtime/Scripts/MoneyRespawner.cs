
using UdonSharp;

namespace MMMaellon.BeefSquashingFightingRing
{
    public class MoneyRespawner : UdonSharpBehaviour
    {
        public SmartObjectSync[] moneyStacks;
        public void Respawn()
        {
            foreach (SmartObjectSync stack in moneyStacks)
            {
                if (stack)
                {
                    stack.Respawn();
                }
            }
        }
    }

}