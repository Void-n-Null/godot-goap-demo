using System.Collections.Generic;
using Game.Data.GOAP;

namespace Game.Data.GOAP;

public interface IStepFactory
{
    List<Step> CreateSteps(State initialState);
}
