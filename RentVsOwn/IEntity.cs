﻿using RentVsOwn.Output;

namespace RentVsOwn
{
    public interface IEntity
    {
        string GenerateReport();

        void Simulate(ISimulation simulation, IOutput output);
    }
}
