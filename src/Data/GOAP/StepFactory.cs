using System.Collections.Generic;
using Game.Data.GOAP;

namespace Game.Data.GOAP;

public interface IStepFactory
{
    public List<Step> CreateSteps(State initialState);
}
