﻿#if false
// TODO: Code needs work

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using JetBrains.Annotations;
using RentVsOwn.Financials;
using RentVsOwn.Output;
using RentVsOwn.Reporting;

namespace RentVsOwn
{
    public sealed class Landlord : IEntity
    {
        [PublicAPI]
        private sealed class Data
        {
            [ReportColumn(Format = ReportColumnFormat.Currency, Grouping = ReportColumnGrouping.Sum, CalculateSum = true, CalculateAverage = true, IncludePeriod0 = false)]
            public decimal Expenses => Rent + RentersInsurance;

            [ReportColumn(Format = ReportColumnFormat.Currency, Grouping = ReportColumnGrouping.Sum, CalculateSum = true, CalculateAverage = true, IncludePeriod0 = false)]
            public decimal Rent { get; set; }

            [ReportColumn(Format = ReportColumnFormat.Currency, Grouping = ReportColumnGrouping.Sum, CalculateSum = true, CalculateAverage = true, IncludePeriod0 = false)]
            public decimal RentersInsurance { get; set; }

            [ReportColumn(Format = ReportColumnFormat.Currency, Grouping = ReportColumnGrouping.Sum, CalculateNpv = true, CalculateIrr = true)]
            public decimal CashFlow { get; set; }
        }
        /// <summary>
        ///     A class to help with monthly calculations.
        /// </summary>
        private sealed class Monthly
        {
            public decimal Rent { get; set; }

            public decimal Expenses { get; set; }

            public decimal Principal { get; set; }

            public decimal Interest { get; set; }

            public decimal NetIncome => Rent - Expenses;

            /// <summary>
            ///     Gets or sets the cash on hand this month.
            ///     This starts with the value of the rent and then gets progressively decreased.
            ///     If we go negative, we need a personal loan.
            /// </summary>
            /// <value>The cash.</value>
            public decimal Cash { get; set; }

            public decimal PersonalLoan { get; set; }

            public decimal NpvCashFlow { get; set; }
        }
        public Renter(ISimulation simulation, IOutput output)
            :base(simulation, output)
        {
        }


        private decimal _netWorth;

        private decimal _initialInvestment;

        /// <summary>
        ///     The basis in the home.
        /// </summary>
        private decimal _basis;

        private decimal _cash;

        private decimal _totalRent;

        private decimal _averageRent;

        private decimal _totalExpenses;

        private decimal _averageExpenses;

        private decimal _carryOverDepreciation;

        private decimal _totalUsedDepreciation;

        /// <summary>
        ///     This represents a personal loan we either had to take out this
        ///     month to make cash flow or an amount we paid back to against a previous personal loan
        /// </summary>
        private decimal _personalLoan;

        /// <summary>
        /// </summary>
        private decimal _currentYearTaxableIncome;

        private double? _npv;

        private double? _irr;

        private List<double> _cashFlows = new List<double>();

        private List<Monthly> _months = new List<Monthly>();
        private readonly Report<Data> _report = new Report<Data>();


        private void CalculateExpenses(Monthly monthly, ISimulation simulation, IOutput output)
        {
            if (simulation.LandlordManagementFeePercentagePerMonth > 0)
            {
                var managementFee = simulation.CurrentRentPerMonth * simulation.LandlordManagementFeePercentagePerMonth;
                monthly.Expenses += managementFee;
                monthly.Cash -= managementFee;
                output.WriteLine($"* Management fee of {managementFee:C0}");
            }

            if (simulation.PropertyTaxPercentagePerYear > 0)
            {
                var propertyTax = (simulation.LandlordHomeValue * simulation.PropertyTaxPercentagePerYear / 12).ToDollars();
                monthly.Expenses += propertyTax;
                monthly.Cash -= propertyTax;
                output.WriteLine($"* Spent {propertyTax:C0} on property tax");
            }

            if (simulation.InsurancePerMonth > 0)
            {
                monthly.Expenses += simulation.InsurancePerMonth;
                monthly.Cash -= simulation.InsurancePerMonth;
                output.WriteLine($"* Spent {simulation.InsurancePerMonth:C0} on insurance");
            }

            if (simulation.HoaPerMonth > 0)
            {
                monthly.Expenses += simulation.HoaPerMonth;
                monthly.Cash -= simulation.HoaPerMonth;
                output.WriteLine($"* Spent {simulation.HoaPerMonth:C0} on HOA");
            }

            if (simulation.HomeMaintenancePercentagePerYear > 0)
            {
                var homeMaintenance = (simulation.LandlordHomeValue * simulation.HomeMaintenancePercentagePerYear / 12).ToDollars();
                monthly.Expenses += homeMaintenance;
                monthly.Cash -= homeMaintenance;
                output.WriteLine($"* Spent {homeMaintenance:C0} on home maintenance");
            }

            // If we have a personal loan, pay interest on it.
            if (_personalLoan > 0)
            {
                var personalLoanInterest = (_personalLoan * simulation.DiscountRatePerYear / 12).ToDollars();
                monthly.Expenses += personalLoanInterest;
                monthly.Cash -= personalLoanInterest;
                output.WriteLine($"* Spent {personalLoanInterest:C0} on interest on personal loan");
            }

            // We pay the previous years taxes at the start of each new year.
            if (simulation.IsNewYear && _currentYearTaxableIncome > 0)
            {
                var taxes = (_currentYearTaxableIncome * simulation.MarginalTaxRatePerYear).ToDollars();
                if (taxes > 0)
                {
                    monthly.Expenses += taxes;
                    monthly.Cash -= taxes;
                    output.WriteLine($"* Paid last year's taxes of {taxes:C0}");
                }

                _currentYearTaxableIncome = 0;
            }

            output.WriteLine($"* Total monthly expenses of {monthly.Expenses:C0} leaving cash of {monthly.Cash:C0}");
            _totalExpenses += monthly.Expenses;
        }

        private void CalculateTaxableIncome(Monthly monthly, ISimulation simulation, IOutput output)
        {
            // My net income includes amount I have paid in principle.
            var taxableIncome = monthly.NetIncome.ToDollars();
            output.WriteLine($"* Net taxable income of {taxableIncome:C0}");

            // We now have a net income for the month.
            // Let's see what we can do about using depreciation
            var depreciation = (simulation.HomePurchaseAmount * simulation.DepreciablePercentage / (simulation.DepreciationYears * 12)).ToDollars();
            output.WriteLine($"* Allowed monthly depreciation of {depreciation:C0} + carry-over of {_carryOverDepreciation:C0}");
            _carryOverDepreciation += depreciation;

            if (taxableIncome > 0)
            {
                var usedDepreciation = Math.Min(taxableIncome, _carryOverDepreciation).ToDollars();
                taxableIncome -= usedDepreciation;
                _carryOverDepreciation -= usedDepreciation;
                _totalUsedDepreciation += usedDepreciation;
                output.WriteLine($"* Used depreciation of {usedDepreciation:C0} resulting in adjusted taxable income of {taxableIncome:C0}");
            }

            if (_carryOverDepreciation > 0)
                output.WriteLine($"* Carry over depreciation of {_carryOverDepreciation:C0}");
            if (taxableIncome > 0)
            {
                _currentYearTaxableIncome += taxableIncome;
                output.WriteLine($"* Taxable income of {taxableIncome:C0} ({_currentYearTaxableIncome:C0} current year)");
            }
        }

        private void CloseOutPersonalLoan(ref decimal proceeds, IOutput output)
        {
            if (_personalLoan != 0)
            {
                proceeds -= _personalLoan;
                output.WriteLine($"* Closed out personal loan of {_personalLoan:C0}");
                _personalLoan = 0;
            }
        }

        private void Finalize(Monthly monthly, ISimulation simulation, IOutput output)
        {
            var proceeds = SellHome(simulation, output);
            PayTaxesOnHomeSale(ref proceeds, simulation, output);
            PayOffLoan(ref proceeds, simulation, output);
            PayRemainingIncomeTax(ref proceeds, simulation, output);
            CloseOutPersonalLoan(ref proceeds, output);
            output.WriteLine($"* Net home sale proceeds of {proceeds:C0}");

            // Put proceeds into cash account
            _cash += proceeds;
            _cash = _cash.ToDollars();
            output.WriteLine($"* Total cash on hand of {_cash:C0}");

            // Add in this very last cash flow
            _cashFlows[_cashFlows.Count - 1] += (double)proceeds;
            output.WriteLine($"* Adjusted NPV cash flow of {_cashFlows[_cashFlows.Count - 1]:C0} accounting for sale proceeds of {proceeds:C0}");
            monthly.NpvCashFlow = (decimal)_cashFlows[_cashFlows.Count - 1];
        }

        public string GenerateReport(ReportGrouping grouping, ReportFormat format)
            => _report.Generate(grouping, format);

        /// <inheritdoc />
        public void NextYear(ISimulation simulation, IOutput output)
        {
            output.WriteLine(Simulation.Separator);
            output.WriteLine($"{Name} Year # {simulation.Month / 12}{Environment.NewLine}");

            if (HomeAppreciationPercentagePerYear > 0)
            {
                OwnerHomeValue = (OwnerHomeValue + OwnerHomeValue * HomeAppreciationPercentagePerYear).ToDollars();
                output.WriteLine($"* Owner Home value increased {HomeAppreciationPercentagePerYear:P2} to {OwnerHomeValue:C0}");
                LandlordHomeValue = (LandlordHomeValue + LandlordHomeValue * HomeAppreciationPercentagePerYear).ToDollars();
                output.WriteLine($"* Landlord Home value increased {HomeAppreciationPercentagePerYear:P2} to {LandlordHomeValue:C0}");
            }

            if (InflationRatePerYear > 0)
            {
                RentersInsurancePerMonth = (RentersInsurancePerMonth + RentersInsurancePerMonth * InflationRatePerYear).ToDollarCents();
                output.WriteLine($"* Renters insurance increased {InflationRatePerYear:P2} to {RentersInsurancePerMonth:C2}");
                InsurancePerMonth = (InsurancePerMonth + InsurancePerMonth * InflationRatePerYear).ToDollarCents();
                output.WriteLine($"* Home owner's insurance increased {InflationRatePerYear:P2} to {InsurancePerMonth:C2}");
                HoaPerMonth = (HoaPerMonth + HoaPerMonth * InflationRatePerYear).ToDollarCents();
                output.WriteLine($"* HOA increased {InflationRatePerYear:P2} to {HoaPerMonth:C2}");
            }
        }

        private void Initialize(ISimulation simulation, IOutput output)
        {
            _initialInvestment = 0;
            _cash = 0;
            _totalRent = 0;
            _averageRent = 0;
            _totalExpenses = 0;
            _averageExpenses = 0;
            _carryOverDepreciation = 0;
            _totalUsedDepreciation = 0;
            _personalLoan = 0;
            _currentYearTaxableIncome = 0;
            _cashFlows = new List<double>();
            _months = new List<Monthly>();
            _npv = null;
            _irr = null;

            _basis = simulation.LandlordHomeValue;
            output.WriteLine($"* Down payment of {simulation.LandlordDownPayment:C0}");
            _initialInvestment += simulation.LandlordDownPayment;
            output.WriteLine($"* Fixed closing costs of {simulation.BuyerFixedCosts:C0}");
            _initialInvestment += simulation.BuyerFixedCosts;
            _basis -= simulation.BuyerFixedCosts;
            var variableClosingCosts = (simulation.LandlordLoanAmount * simulation.BuyerVariableCostsPercentage).ToDollars();
            output.WriteLine($"* Variable closing costs of {variableClosingCosts:C0}");
            _initialInvestment += variableClosingCosts;
            _basis -= variableClosingCosts;
            output.WriteLine($"* Total initial investment of {_initialInvestment:C0}");
            output.WriteLine($"* Basis in home purchase {_basis:C0}");
            output.WriteLine($"* Initial loan balance of {simulation.LandlordLoanBalance:C0}");

            // TODO: Code needs work
#if false
     Report.AddNotes
            _report.DiscountRatePerYear = simulation.DiscountRatePerYear;
            _report.AddNote(output.WriteLine($"* {_report.DiscountRatePerYear:P2} annual discount rate; {Financial.ConvertDiscountRateYearToMonth(_report.DiscountRatePerYear):P4} monthly discount rate"));
            _report.Add(new Data
            {
                CashFlow = -_initialCash,
            }); 
#endif

        }

        private Monthly InitializeMonthly(ISimulation simulation, IOutput output)
        {
            // Set up our monthly ledger
            var monthly = new Monthly
            {
                Rent = simulation.CurrentRentPerMonth,
                Interest = 0,
                Principal = 0,
                Expenses = 0,
                Cash = simulation.CurrentRentPerMonth,
                PersonalLoan = 0,
            };

            _totalRent += monthly.Rent;
            output.WriteLine($"* Received rent of {monthly.Rent:C0}");

            // See if we are still paying on loan.
            if (simulation.LandlordLoanBalance > 0)
            {
                var loanPayment = simulation.LandlordMonthlyPayment;
                monthly.Interest = (simulation.LandlordLoanBalance * simulation.LandlordInterestRate / 12).ToDollars();
                monthly.Principal = Math.Min(loanPayment - monthly.Interest, simulation.LandlordLoanBalance).ToDollars();

                output.WriteLine($"* Loan payment of {loanPayment:C0} ({monthly.Principal:C0} principal / {monthly.Interest:C0} interest)");

                simulation.LandlordLoanBalance -= monthly.Principal;
                output.WriteLine($"* New loan balance of {simulation.LandlordLoanBalance:C0}");

                monthly.Cash -= monthly.Interest;
                monthly.Expenses += monthly.Interest;
                monthly.Cash -= monthly.Principal;
            }

            return monthly;
        }

        private static void PayDownLoan(Monthly monthly, ISimulation simulation, IOutput output)
        {
            // If we have any cash left, use it to pay down the mortgage balance.
            if (monthly.Cash > 0 && simulation.LandlordLoanBalance > 0)
            {
                var additionalPrincipal = Math.Min(simulation.LandlordLoanBalance, monthly.Cash);
                simulation.LandlordLoanBalance -= additionalPrincipal;
                monthly.Cash -= additionalPrincipal;
                output.WriteLine($"* Paid additional principal of {additionalPrincipal:C0} leaving balance of {simulation.LandlordLoanBalance:C0} and cash of {monthly.Cash:C0}");
            }
        }

        private static void PayOffLoan(ref decimal proceeds, ISimulation simulation, IOutput output)
        {
            if (simulation.LandlordLoanBalance > 0)
            {
                output.WriteLine($"* Paid off loan balance of {simulation.LandlordLoanBalance:C0}");
                proceeds -= simulation.LandlordLoanBalance;
                simulation.LandlordLoanBalance = 0;
            }

            simulation.LandlordHomeValue = 0;
        }

        private void PayRemainingIncomeTax(ref decimal proceeds, ISimulation simulation, IOutput output)
        {
            if (_currentYearTaxableIncome > 0)
            {
                var taxes = (_currentYearTaxableIncome * simulation.MarginalTaxRatePerYear).ToDollars();
                if (taxes > 0)
                {
                    proceeds -= taxes;
                    _totalExpenses += taxes;
                    output.WriteLine($"* Paid remaining yearly taxes of {taxes:C0}");
                }

                _currentYearTaxableIncome = 0;
            }
        }

        private void PayTaxesOnHomeSale(ref decimal proceeds, ISimulation simulation, IOutput output)
        {
            var capitalGains = proceeds - _basis;
            if (capitalGains > 0)
            {
                output.WriteLine($"* Total gain from sale of {capitalGains:C0}");
                if (_totalUsedDepreciation > 0)
                {
                    var reclaimedDepreciation = Math.Min(capitalGains, _totalUsedDepreciation).ToDollars();
                    var reclaimedDepreciationTaxes = (reclaimedDepreciation * simulation.MarginalTaxRatePerYear).ToDollars();
                    if (reclaimedDepreciationTaxes > 0)
                    {
                        proceeds -= reclaimedDepreciationTaxes;
                        capitalGains -= reclaimedDepreciation;
                        _totalExpenses += reclaimedDepreciationTaxes;
                        _totalUsedDepreciation -= reclaimedDepreciation;
                        output.WriteLine($"* Paid depreciation recapture taxes of {reclaimedDepreciationTaxes:C0} on {reclaimedDepreciation:C0}");
                    }
                }

                if (capitalGains > 0)
                {
                    var capitalGainsTaxes = (capitalGains * simulation.CapitalGainsRatePerYear).ToDollars();
                    proceeds -= capitalGainsTaxes;
                    _totalExpenses += capitalGainsTaxes;
                    output.WriteLine($"* Paid capital gains taxes of {capitalGainsTaxes:C0} on {capitalGains:C0}");
                }
            }
        }

        private Monthly Process(ISimulation simulation, IOutput output)
        {
            var monthly = InitializeMonthly(simulation, output);
            CalculateExpenses(monthly, simulation, output);
            CalculateTaxableIncome(monthly, simulation, output);
            RepayPersonalLoan(monthly, output);
            PayDownLoan(monthly, simulation, output);
            TakePersonalLoan(monthly, output);
            RecordCashFlow(monthly, output);
            return monthly;
        }

        private void RecordCashFlow(Monthly monthly, IOutput output)
        {
            // Cash flow used for NPV
            if (monthly.Expenses + monthly.Principal > monthly.Rent)
            {
                output.WriteLine($"* Monthly expenses {monthly.Expenses:C0} + principle of {monthly.Principal:C0} = {monthly.Expenses + monthly.Principal:C0} against rent of {monthly.Rent:C0}");
                output.WriteLine($"* Negative cash flow of {monthly.Rent - (monthly.Expenses + monthly.Principal):C0}");
            }

            // monthly.PersonalLoan could be positive or negative depending on whether we took out loan or repaid one.
            // Essentially, the personal loan causes us to defer cash flow (for NPV/IRR) until we actually payback the loan.
            var cashFlow = monthly.NetIncome - monthly.PersonalLoan;
            monthly.NpvCashFlow = cashFlow;
            _cashFlows.Add((double)cashFlow);
            output.WriteLine($"* NPV cash flow of {cashFlow:C0}");
        }

        private void RepayPersonalLoan(Monthly monthly, IOutput output)
        {
            if (monthly.Cash > 0 && _personalLoan > 0)
            {
                var personalLoanPayment = Math.Min(_personalLoan, monthly.Cash);
                _personalLoan -= personalLoanPayment;
                monthly.Cash -= personalLoanPayment;
                monthly.PersonalLoan = -personalLoanPayment;
                output.WriteLine($"* Paid back {personalLoanPayment:C0} of personal loan leaving a balance of {_personalLoan:C0} and cash of {monthly.Cash:C0}");
            }
        }

        private decimal SellHome(ISimulation simulation, IOutput output)
        {
            var homeValue = simulation.LandlordHomeValue;
            output.WriteLine($"* Sold home for {homeValue:C0}");
            var salesFixedCosts = simulation.SellerFixedCosts;
            var salesCommission = simulation.SellerCommissionPercentage * homeValue;
            var proceeds = homeValue - (salesFixedCosts + salesCommission);
            output.WriteLine($"* Fixed sales costs of {salesFixedCosts:C0} and commission of {salesCommission:C0} leaving proceeds of {proceeds:C0}");
            _totalExpenses += salesFixedCosts + salesCommission;
            return proceeds;
        }

        /// <inheritdoc />
        public void Simulate(ISimulation simulation, IOutput output)
        {
            if (simulation == null)
                throw new ArgumentNullException(nameof(simulation));
            if (output == null)
                throw new ArgumentNullException(nameof(output));

            output.WriteLine($"{Name} in month # {simulation.Month}{Environment.NewLine}");

            if (simulation.IsInitialMonth)
                Initialize(simulation, output);
            var monthly = Process(simulation, output);
            if (simulation.IsFinalMonth)
                Finalize(monthly, simulation, output);

            _months.Add(monthly);

            _averageRent = (_totalRent / simulation.Month).ToDollars();
            _averageExpenses = (_totalExpenses / simulation.Month).ToDollars();
            _netWorth = _cash - _personalLoan + simulation.LandlordHomeValue - simulation.LandlordLoanBalance;
            // TODO: Code needs work
#if false
                 _report.Add(data); 
#endif
        }

        private void TakePersonalLoan(Monthly monthly, IOutput output)
        {
            if (monthly.Cash < 0)
            {
                var personalLoanAmount = Math.Abs(monthly.Cash);
                _personalLoan += personalLoanAmount;
                monthly.PersonalLoan = personalLoanAmount;
                output.WriteLine($"* Required personal loan of {personalLoanAmount:C0} creating a balance of {_personalLoan:C0}");

                monthly.Cash = 0;
            }
        }

        /// <inheritdoc />
        public override string ToString()
        {
            var text = new StringBuilder();
            text.AppendLine(
                $"{Name} received total rent of {_totalRent:C0} (average of {_averageRent:C0} / month), total expenses of {_totalExpenses:C0} (average of {_averageExpenses:C0} / month), and has net worth of {_netWorth:C0} on initial investment of {_initialInvestment:C0}");
            if (_npv.HasValue)
                text.AppendLine($"Net present value of {_npv:C0}");
            if (_irr.HasValue)
                text.AppendLine($"Internal rate of return of {_irr:P2}");

            return text.ToString().TrimEnd();
        }
    }
}
#endif
