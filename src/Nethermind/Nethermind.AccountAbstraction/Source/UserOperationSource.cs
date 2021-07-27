//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
// 

using System.Collections.Generic;
using System.Linq;
using Nethermind.AccountAbstraction.Data;

namespace Nethermind.AccountAbstraction.Source
{
    public class UserOperationSource : IUserOperationSource
    {
        private readonly UserOperationSortedPool _userOperationSortedPool;

        public UserOperationSource(UserOperationSortedPool userOperationSortedPool)
        {
            _userOperationSortedPool = userOperationSortedPool;
        }

        public IEnumerable<UserOperation> GetUserOperations()
        {
            UserOperation[] ops = _userOperationSortedPool.GetSnapshot().ToArray();
            foreach (var op in ops.OrderByDescending(op => op.GasPrice))
            {
                yield return op;
            }
        }
    }
}
